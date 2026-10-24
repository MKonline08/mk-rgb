using MkRgb.Core;

namespace MkRgb.Hardware;

/// <summary>Routes trusted adapters while giving every extension a separate identifier namespace.</summary>
public sealed class HardwareHub(string directory) : ISensorProvider, IFanController, IDisposable
{
    readonly Sensors builtin = new();
    readonly Extensions extensions = new(directory);
    readonly Dictionary<string, (IFanController Owner, string Native)> channels = [];
    readonly Dictionary<string, (IFanController Owner, string Native)> owned = [];
    static string Prefix(object adapter) => "extension:" + adapter.GetType().Assembly.GetName().Name + ":" + adapter.GetType().FullName + ":";
    public HardwareSnapshot Read()
    {
        var snapshot = builtin.Read();
        channels.Clear();
        foreach (var f in snapshot.Fans)
            channels[f.Id] = (builtin, f.Id);
        snapshot.Warnings.AddRange(extensions.Errors);
        foreach (var provider in extensions.SensorProviders)
        {
            try
            {
                var data = provider.Read();
                var prefix = Prefix(provider);
                snapshot.Devices.AddRange(data.Devices.Select(d => d with { Id = prefix + d.Id, Verified = false }));
                snapshot.Sensors.AddRange(data.Sensors.Select(s => s with { Id = prefix + s.Id, DeviceId = prefix + s.DeviceId }));
                foreach (var f in data.Fans)
                {
                    var controller = provider as IFanController;
                    if (controller != null && extensions.FanControllers.Contains(controller))
                        channels[prefix + f.Id] = (controller, f.Id);
                    snapshot.Fans.Add(f with
                    {
                        Id = prefix + f.Id,
                        DeviceId = prefix + f.DeviceId,
                        RpmSensorId = f.RpmSensorId is null ? null : prefix + f.RpmSensorId,
                        Writable = controller != null && extensions.FanControllers.Contains(controller) && f.Writable
                    });
                }
                snapshot.Warnings.AddRange(data.Warnings);
            }
            catch (Exception e) { snapshot.Warnings.Add(provider.GetType().Name + ": " + e.Message); }
        }
        return snapshot;
    }
    public void SetDuty(string id, double duty)
    {
        if (!double.IsFinite(duty) || duty is < 0 or > 100)
            throw new InvalidDataException("Invalid fan duty.");
        if (!channels.TryGetValue(id, out var channel))
            throw new InvalidOperationException("Control channel disconnected.");
        if (channel.Owner is IFanWatchdog watchdog)
            watchdog.ArmFallback(channel.Native, TimeSpan.FromSeconds(5), 100);
        owned[id] = channel;
        channel.Owner.SetDuty(channel.Native, duty);
    }
    public void Restore(string id)
    {
        if (!owned.TryGetValue(id, out var channel))
            return;
        channel.Owner.Restore(channel.Native);
        owned.Remove(id);
    }
    public void RestoreAll()
    {
        List<Exception> errors = [];
        foreach (var id in owned.Keys.ToList())
            try
            {
                Restore(id);
            }
            catch (Exception e) { errors.Add(e); }
        if (errors.Count > 0)
            throw new AggregateException("Some channels could not restore default control.", errors);
    }
    public void Dispose()
    {
        try
        {
            RestoreAll();
        }
        finally { builtin.Dispose(); extensions.Dispose(); }
    }
}
