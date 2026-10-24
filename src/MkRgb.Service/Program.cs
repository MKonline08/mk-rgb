using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MkRgb.Core;
using MkRgb.Hardware;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Diagnostics;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "MkRgbHardware");
var sidIndex = Array.IndexOf(args, "--owner-sid");
var ownerSid = sidIndex >= 0 && args.Length > sidIndex + 1 ? args[sidIndex + 1] : WindowsIdentity.GetCurrent().User!.Value;
builder.Services.AddSingleton(new Owner(ownerSid));
builder.Services.AddHostedService<HardwareService>();
await builder.Build().RunAsync();
record Owner(string Sid);

sealed class HardwareService(Owner owner, ILogger<HardwareService> log) : BackgroundService
{
    readonly HardwareHub sensors = new(AppContext.BaseDirectory); readonly CoolingEngine cooling = new(); readonly SemaphoreSlim gate = new(1);
    readonly System.Collections.Concurrent.ConcurrentDictionary<int, Task> handlers = new(); int nextHandler;
    HardwareSnapshot snapshot = new([], [], [], [], DateTimeOffset.UtcNow);
    List<FanPolicy> policies = []; Dictionary<string, CalibrationResult> calibrated = []; Process? rgb;
    string? calibrationChannel; string? lastFault;
    string PoliciesPath => Path.Combine(Paths.MachineData, "cooling.json");
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        Directory.CreateDirectory(Paths.MachineData);
        var directorySecurity = new DirectorySecurity();
        directorySecurity.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { "S-1-5-18", "S-1-5-32-544" })
            directorySecurity.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(Paths.MachineData).SetAccessControl(directorySecurity);
        try
        {
            if (File.Exists(PoliciesPath))
                policies = Json.Read<List<FanPolicy>>(File.ReadAllText(PoliciesPath));
            if (File.Exists(PoliciesPath + ".calibration"))
                calibrated = Json.Read<Dictionary<string, CalibrationResult>>(File.ReadAllText(PoliciesPath + ".calibration"));
        }
        catch (Exception e) { lastFault = "Saved cooling configuration rejected: " + e.Message; policies = []; }
        snapshot = sensors.Read();
        try
        {
            Apply(policies);
        }
        catch (Exception e) { policies = []; lastFault = "Saved cooling configuration rejected: " + e.Message; }
        var listener = Listen(token);
        try
        {
            while (!token.IsCancellationRequested)
            {
                await gate.WaitAsync(token);
                try
                {
                    snapshot = sensors.Read();
                    foreach (var policy in policies.Where(p => p.Enabled && p.ChannelId != calibrationChannel))
                    {
                        if (!calibrated.ContainsKey(policy.ChannelId))
                            continue;
                        try
                        {
                            var duty = cooling.Evaluate(policy, snapshot.Sensors, DateTimeOffset.UtcNow);
                            var validation = calibrated[policy.ChannelId];
                            var rpm = snapshot.Sensors.FirstOrDefault(s => s.Id == validation.RpmSensorId);
                            if (duty > 0 && (rpm is null || !rpm.IsFresh(DateTimeOffset.UtcNow) || rpm.Value <= 0))
                                duty = 100;
                            sensors.SetDuty(policy.ChannelId, duty);
                        }
                        catch (Exception e) { lastFault = "Cooling: " + e.Message; try { sensors.SetDuty(policy.ChannelId, 100); } catch { sensors.Restore(policy.ChannelId); } }
                    }
                    if (lastFault != null)
                        snapshot.Warnings.Add(lastFault);
                }
                finally { gate.Release(); }
                await Task.Delay(1000, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            await listener;
            await Task.WhenAll(handlers.Values);
            try
            {
                sensors.RestoreAll();
            }
            catch (Exception e) { log.LogError(e, "Default fan control could not be restored"); }
            if (rgb is { HasExited: false })
                rgb.Kill(true);
            sensors.Dispose();
        }
    }
    async Task Listen(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var acl = new PipeSecurity();
            acl.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { owner.Sid, "S-1-5-18", "S-1-5-32-544" })
                acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sid), sid == "S-1-5-18" ? PipeAccessRights.FullControl : PipeAccessRights.ReadWrite, AccessControlType.Allow));
            var pipe = NamedPipeServerStreamAcl.Create(PipeClient.Name, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, acl);
            try
            {
                await pipe.WaitForConnectionAsync(token);
                var id = Interlocked.Increment(ref nextHandler);
                var task = Handle(pipe, token);
                handlers[id] = task;
                _ = task.ContinueWith(_ => handlers.TryRemove(id, out var ignored), TaskScheduler.Default);
            }
            catch (OperationCanceledException) { pipe.Dispose(); break; }
            catch (Exception e) { pipe.Dispose(); log.LogError(e, "Pipe accept failed"); await Task.Delay(1000, token); }
        }
    }
    async Task Handle(NamedPipeServerStream pipe, CancellationToken stop)
    {
        await using var owned = pipe;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        try
        {
            var request = Json.Read<RpcRequest>(await Framing.Read(pipe, token));
            if (request.Version != 1)
                throw new InvalidDataException("Unsupported API version.");
            object result;
            if (request.Method == "calibrate")
                result = await Calibrate(Json.Read<CalibrationRequest>(request.Payload), token);
            else
            {
                await gate.WaitAsync(token);
                try
                {
                    result = request.Method switch
                    {
                        "snapshot" => snapshot,
                        "apply" => Apply(Json.Read<List<FanPolicy>>(request.Payload)),
                        "restore" => Restore(),
                        "startRgb" => StartRgb(),
                        "calibrations" => calibrated,
                        _ => throw new InvalidDataException("Unknown service operation.")
                    };
                }
                finally { gate.Release(); }
            }
            await Framing.Write(pipe, Json.Write(new RpcResponse(true, Json.Write(result))), token);
        }
        catch (Exception e)
        {
            log.LogWarning("RPC: {Message}", e.Message);
            try
            {
                await Framing.Write(pipe, Json.Write(new RpcResponse(false, "", e.Message)), stop);
            }
            catch (IOException) { }
            catch (OperationCanceledException) { }
        }
    }
    bool Apply(List<FanPolicy> incoming)
    {
        if (calibrationChannel != null)
            throw new InvalidOperationException("Wait for commissioning to finish before applying profiles.");
        SettingsStore.ValidateProfile(new()
        {
            Fans = incoming
        });
        if (incoming.Select(f => f.ChannelId).Distinct().Count() != incoming.Count)
            throw new InvalidDataException("Duplicate fan channels.");
        foreach (var p in incoming.Where(p => p.Enabled))
        {
            if (!calibrated.TryGetValue(p.ChannelId, out var evidence) || !snapshot.Fans.Any(f => f.Id == p.ChannelId && f.Writable && f.CanRestore))
                throw new InvalidOperationException("Commission this channel before applying control.");
            if (!double.IsFinite(evidence.Minimum) || evidence.Minimum is < 1 or > 100 || !double.IsFinite(evidence.StartDuty) || evidence.StartDuty < evidence.Minimum || evidence.StartDuty > 100)
                throw new InvalidDataException("Invalid saved channel calibration.");
            if (p.Sources.Count == 0 || p.Sources.Any(s => !snapshot.Sensors.Any(r => r.Id == s.SensorId && r.Kind == SensorKind.Temperature)))
                throw new InvalidDataException("Map fresh temperature sources before enabling cooling.");
            if (p.Sources.Any(s => s.Points.Max(x => x.Temperature) > 85 || s.Points.OrderBy(x => x.Temperature).Last().Duty != 100))
                throw new InvalidDataException("A protective 100% point at or below 85 °C is required.");
            p.Minimum = Math.Max(p.Minimum, evidence.Minimum);
            p.StartDuty = Math.Max(p.StartDuty, evidence.StartDuty);
            p.Calibrated = true;
            p.Pump = evidence.Pump;
            p.AllowStop = p.AllowStop && evidence.CanStop && !p.Pump;
        }
        foreach (var p in policies.Where(p => p.Enabled && !incoming.Any(i => i.ChannelId == p.ChannelId && i.Enabled)))
            sensors.Restore(p.ChannelId);
        SettingsStore.AtomicWrite(PoliciesPath, Json.Write(incoming));
        policies = incoming;
        return true;
    }
    bool Restore()
    {
        if (calibrationChannel != null)
            throw new InvalidOperationException("Wait for commissioning to finish before restoring control.");
        SettingsStore.AtomicWrite(PoliciesPath, "[]");
        policies = [];
        sensors.RestoreAll();
        return true;
    }
    bool StartRgb()
    {
        if (rgb is { HasExited: false })
            return true;
        var conflicts = Sensors.Conflicts();
        if (conflicts.Count > 0)
            throw new InvalidOperationException("Close competing hardware controllers first: " + string.Join(", ", conflicts));
        if (Process.GetProcessesByName("OpenRGB").Any())
            throw new InvalidOperationException("An existing OpenRGB process is running. Close it before using the bundled controller.");
        var exe = Path.Combine(AppContext.BaseDirectory, "OpenRGB", "OpenRGB.exe");
        if (!File.Exists(exe))
            throw new FileNotFoundException("Bundled OpenRGB is missing. Repair the MK RGB installation.");
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "--server", "--server-host", "127.0.0.1", "--server-port", "6743", "--noautoconnect", "--config", Path.Combine(Paths.MachineData, "OpenRGB") })
            info.ArgumentList.Add(arg);
        rgb = Process.Start(info);
        return true;
    }
    async Task<CalibrationResult> Calibrate(CalibrationRequest request, CancellationToken token)
    {
        if (!request.Confirmed)
            throw new InvalidOperationException("Commissioning needs confirmation of the physical channel and temperature source.");
        await gate.WaitAsync(token);
        try
        {
            if (calibrationChannel != null)
                throw new InvalidOperationException("Another channel is being commissioned.");
            var channel = snapshot.Fans.FirstOrDefault(f => f.Id == request.ChannelId) ?? throw new InvalidOperationException("Channel not found.");
            if (!channel.CanRestore || !channel.Writable)
                throw new InvalidOperationException("No default-control recovery is available for this channel.");
            if (channel.Pump && !request.IsPump)
                throw new InvalidOperationException("Pump channels must be commissioned as pumps.");
            if (request.TestStop && (request.IsPump || channel.Pump || channel.Min > 0))
                throw new InvalidOperationException("Stop testing is unavailable for pumps or channels with a nonzero hardware minimum.");
            if (!snapshot.Sensors.Any(s => s.Id == request.RpmSensorId && s.Kind == SensorKind.Fan) || !snapshot.Sensors.Any(s => s.Id == request.TemperatureSensorId && s.Kind == SensorKind.Temperature))
                throw new InvalidOperationException("Select an RPM sensor and temperature sensor.");
            calibrationChannel = request.ChannelId;
        }
        finally { gate.Release(); }
        try
        {
            var minimum = request.IsPump ? 70d : 30d;
            var highRpm = 0d;
            var lowRpm = 0d;
            foreach (var duty in request.IsPump ? new[] { 100d, 85, 70 } : new[] { 100d, 85, 70, 55, 40, 30 })
            {
                await gate.WaitAsync(token);
                try
                {
                    sensors.SetDuty(request.ChannelId, duty);
                }
                finally { gate.Release(); }
                await Task.Delay(3000, token);
                await gate.WaitAsync(token);
                try
                {
                    var s = sensors.Read();
                    var t = s.Sensors.FirstOrDefault(s => s.Id == request.TemperatureSensorId);
                    var rpm = s.Sensors.FirstOrDefault(s => s.Id == request.RpmSensorId);
                    if (t is null || !t.IsFresh(DateTimeOffset.UtcNow) || t.Value >= 75)
                        throw new InvalidOperationException("Commissioning stopped: temperature is unavailable or at least 75 °C.");
                    if (rpm is null || !rpm.IsFresh(DateTimeOffset.UtcNow))
                        throw new InvalidOperationException("RPM feedback is unavailable.");
                    if (duty == 100)
                        highRpm = rpm.Value ?? 0;
                    if (rpm.Value <= 0)
                    {
                        minimum = Math.Min(100, duty + 15);
                        break;
                    }
                    minimum = duty;
                    lowRpm = rpm.Value ?? 0;
                }
                finally { gate.Release(); }
            }
            if (highRpm <= 0 || highRpm - lowRpm < highRpm * .05)
                throw new InvalidOperationException("RPM did not respond enough to validate this channel mapping.");
            var canStop = false;
            var startDuty = Math.Min(100, minimum + 15);
            if (request.TestStop)
            {
                async Task<double> CheckRpm()
                {
                    await gate.WaitAsync(token);
                    try
                    {
                        var current = sensors.Read();
                        var temperature = current.Sensors.FirstOrDefault(s => s.Id == request.TemperatureSensorId);
                        var speed = current.Sensors.FirstOrDefault(s => s.Id == request.RpmSensorId);
                        if (temperature?.IsFresh(DateTimeOffset.UtcNow) != true || temperature.Value >= 70 || speed?.IsFresh(DateTimeOffset.UtcNow) != true)
                            throw new InvalidOperationException("Stop test aborted: guard reached 70 °C or feedback is unavailable.");
                        return speed.Value!.Value;
                    }
                    finally { gate.Release(); }
                }
                await CheckRpm();
                await gate.WaitAsync(token);
                try
                {
                    sensors.SetDuty(request.ChannelId, 0);
                }
                finally { gate.Release(); }
                for (var i = 0; i < 8; i++)
                {
                    await Task.Delay(1000, token);
                    canStop = await CheckRpm() == 0;
                    if (canStop)
                        break;
                }
                await gate.WaitAsync(token);
                try
                {
                    sensors.SetDuty(request.ChannelId, startDuty);
                }
                finally { gate.Release(); }
                var restarted = false;
                for (var i = 0; i < 5; i++)
                {
                    await Task.Delay(1000, token);
                    if (await CheckRpm() > 0)
                    {
                        restarted = true;
                        break;
                    }
                }
                if (!canStop || !restarted)
                    throw new InvalidOperationException("Stop/restart could not be validated; default control restored.");
            }
            var result = new CalibrationResult(request.ChannelId, minimum, startDuty, canStop, DateTimeOffset.UtcNow, request.RpmSensorId, request.TemperatureSensorId, request.IsPump);
            await gate.WaitAsync(token);
            try
            {
                sensors.Restore(request.ChannelId);
                calibrated[request.ChannelId] = result;
                SettingsStore.AtomicWrite(PoliciesPath + ".calibration", Json.Write(calibrated));
            }
            finally { gate.Release(); }
            return result;
        }
        finally { await gate.WaitAsync(CancellationToken.None); try { sensors.Restore(request.ChannelId); } finally { calibrationChannel = null; gate.Release(); } }
    }
}
