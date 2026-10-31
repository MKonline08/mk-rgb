using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Threading;
using MkRgb.Core;
using MkRgb.Hardware;

namespace MkRgb.Desktop;

public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged; public void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public sealed class SensorRow : Observable
{
    public SensorSample Sample
    {
        get; private set;
    }
    public string Id => Sample.Id; public string Name
    {
        get; set;
    }
    public string Device => Sample.DeviceId;
    public string Kind => Sample.Kind.ToString(); public string Value { get; private set; } = "—";
    public double? Min
    {
        get; private set;
    }
    public double? Max
    {
        get; private set;
    }
    public double Average => count == 0 ? 0 : sum / count;
    double sum; long count;
    public void RefreshAvailability()
    {
        if (!Sample.IsFresh(DateTimeOffset.UtcNow))
        {
            Value = "Unavailable";
            Changed(nameof(Value));
        }
    }
    public SensorRow(SensorSample s)
    {
        Sample = s;
        Name = s.Name;
    }
    public void Update(SensorSample s, Settings settings)
    {
        Sample = s;
        Name = settings.SensorNames.GetValueOrDefault(s.Id, s.Name);
        if (s.Value is { } v && double.IsFinite(v))
        {
            Min = Min is null ? v : Math.Min(Min.Value, v);
            Max = Max is null ? v : Math.Max(Max.Value, v);
            sum += v;
            count++;
        }
        var display = s.Value;
        var unit = s.Unit;
        if (settings.Fahrenheit && s.Kind == SensorKind.Temperature)
        {
            display = display * 1.8 + 32;
            unit = "°F";
        }
        Value = s.IsFresh(DateTimeOffset.UtcNow) ? $"{display:0.#} {unit}" : "Unavailable";
        Changed("");
    }
}
public sealed class MainViewModel : Observable, IAsyncDisposable
{
    readonly SettingsStore store = new(Paths.UserData); readonly TelemetryStore telemetry;
    readonly Dispatcher dispatcher = System.Windows.Application.Current.Dispatcher;
    readonly CancellationTokenSource life = new(); readonly UndoHistory<Profile> history = new();
    readonly Stopwatch clock = Stopwatch.StartNew(); readonly Dictionary<string, (double Value, DateTimeOffset At)> events = []; readonly SemaphoreSlim profileSwitch = new(1);
    readonly Dictionary<string, DateTimeOffset> lastAlerts = [];
    readonly Extensions extensions = new(AppContext.BaseDirectory);
    readonly Dictionary<string, (IRgbController Controller, string Native)> extensionRgb = [];
    readonly List<(IEventSource Source, Action<string, double> Handler)> eventHandlers = [];
    readonly SemaphoreSlim rgbConnection = new(1);
    bool settingsWritable = true;
    Sensors? fallback; Task? polling; Task? rendering; Profile profile; string status = "Discovering hardware…"; string connection = "Connecting";
    public Settings Settings
    {
        get;
    }
    public SessionInputs Inputs { get; } = new(); public EventServer Api { get; } = new(); public OpenRgb Rgb { get; } = new();
    public ObservableCollection<Device> Devices { get; } = []; public ObservableCollection<SensorRow> Sensors { get; } = []; public ObservableCollection<FanChannel> Fans { get; } = [];
    public ObservableCollection<CatalogItem> Catalog { get; } = [];
    public HardwareSnapshot Snapshot { get; private set; } = new([], [], [], [], DateTimeOffset.UtcNow);
    public bool LightingEnabled
    {
        get; set;
    }
    public bool ServiceConnected
    {
        get; private set;
    }
    public DateTimeOffset? ManualUntil
    {
        get; set;
    }
    public event Action? ProfileChanged; public event Action? Frame; public event Action<string>? Alert;
    public string Status
    {
        get => status; set
        {
            status = value;
            Changed();
        }
    }
    public string Connection
    {
        get => connection; set
        {
            connection = value;
            Changed();
        }
    }
    public string DeviceCount => Devices.Count.ToString("00"); public string SensorCount => Sensors.Count.ToString("00");
    public string RgbSummary => Devices.Count(d => d.Capabilities.HasFlag(Capability.Rgb)) is var n && n > 0
        ? $"{n} RGB devices found · {Devices.Sum(d => d.Zones.Count(z => NeedsComponentSetup(d, z)))} headers need setup"
        : "Connect your RGB devices to get started";
    public bool NeedsComponentSetup(Device device, Zone zone) => RgbSetup.NeedsSetup(device.Id, zone, Settings.RgbZoneCounts);
    public void PauseLighting()
    {
        LightingEnabled = false;
        Changed(nameof(LightingEnabled));
        Status = "Lighting paused. Devices keep their last color.";
    }
    public async Task ApplyQuickLighting(string? id, EffectKind kind, string color, double brightness, double speed)
    {
        // Validate before connecting or changing the existing composition.
        QuickLighting.Apply(new Profile(), [], kind, color, brightness, speed);
        if (!Devices.Any(d => d.Capabilities.HasFlag(Capability.Rgb)) || Rgb.NeedsDiscovery)
            await ConnectRgb();
        var targets = Devices.Where(d => d.Capabilities.HasFlag(Capability.Rgb) && d.LedCount > 0 && (string.IsNullOrEmpty(id) || d.Id == id)).Select(d => d.Id).ToList();
        if (targets.Count == 0)
            throw new InvalidOperationException("No configured RGB devices are available. Open Devices and configure the connected components.");
        Record();
        var count = QuickLighting.Apply(Profile, targets, kind, color, brightness, speed);
        Save();
        LightingEnabled = true;
        Changed(nameof(LightingEnabled));
        ManualUntil = DateTimeOffset.UtcNow.AddMinutes(15);
        Status = $"Lighting enabled for {count} device(s). Headers with no components configured will stay dark.";
    }
    public async Task ConfigureRgbZone(string id, Zone zone, int count)
    {
        await rgbConnection.WaitAsync(life.Token);
        var resume = LightingEnabled;
        PauseLighting();
        try
        {
            await Rgb.ResizeAsync(id, int.Parse(zone.Id), count, life.Token);
            var inventory = await Rgb.DiscoverAsync(life.Token);
            if (inventory.FirstOrDefault(d => d.Id == id)?.Zones.FirstOrDefault(z => z.Id == zone.Id)?.Count != count)
                throw new IOException("The controller did not accept this component count. Check the selected header and retry.");
            if (!Settings.RgbZoneCounts.TryGetValue(id, out var counts)) Settings.RgbZoneCounts[id] = counts = [];
            counts[zone.Id] = count;
            Save();
            await DiscoverRgb();
            LightingEnabled = resume;
            Changed(nameof(LightingEnabled));
            Status = count == 0 ? "Header marked unused and saved." : $"Saved {count} LEDs on {zone.Name}. Choose a color in Lighting to test them.";
        }
        finally { rgbConnection.Release(); }
    }
    public string Cpu => Sensors.FirstOrDefault(s => s.Sample.Kind == SensorKind.Temperature && s.Sample.Value is > 0 && s.Device.Contains("cpu", StringComparison.OrdinalIgnoreCase))?.Value ?? "Unavailable";
    public string Gpu => Sensors.Where(s => s.Sample.Kind == SensorKind.Temperature && s.Sample.Value is > 0 && s.Device.Contains("gpu", StringComparison.OrdinalIgnoreCase)).OrderByDescending(s => s.Device.Contains("nvidia")).FirstOrDefault()?.Value ?? "Unavailable";
    public Profile Profile
    {
        get => profile;
        set
        {
            if (value is null || value == profile)
                return;
            _ = Run(() => ActivateProfile(value, true));
        }
    }
    public MainViewModel()
    {
        try
        {
            Settings = store.Load();
        }
        catch (Exception e) { Settings = new(); settingsWritable = false; Status = "Settings could not load; saving is disabled to preserve the original file: " + e.Message; }
        profile = Settings.Profiles.FirstOrDefault(p => p.Id == Settings.ActiveProfileId) ?? Settings.Profiles[0];
        telemetry = new(Path.Combine(Paths.UserData, "telemetry.db"));
        telemetry.Prune(Settings.RetentionDays);
    }
    public void Start()
    {
        foreach (var source in extensions.EventSources)
        {
            Action<string, double> handler = (name, value) => dispatcher.BeginInvoke(() =>
            {
                if (!life.IsCancellationRequested && name.Length <= 64 && double.IsFinite(value))
                    events[name] = (value, DateTimeOffset.UtcNow);
            });
            source.EventReceived += handler;
            eventHandlers.Add((source, handler));
        }
        polling = Poll();
        rendering = Render();
        if (Settings.ConnectRgbOnLaunch || Settings.EnableLightingOnLaunch)
            _ = Run(async () =>
            {
                // Poll establishes service availability before starting privileged discovery.
                for (var i = 0; i < 30 && connection == "Connecting"; i++)
                    await Task.Delay(100, life.Token);
                await ConnectRgb();
                LightingEnabled = Settings.EnableLightingOnLaunch;
                Changed(nameof(LightingEnabled));
            });
        _ = Run(LoadCatalog);
        if (Settings.ApiEnabled)
            _ = Run(RestartApi);
    }
    async Task ActivateProfile(Profile value, bool manual)
    {
        await profileSwitch.WaitAsync(life.Token);
        try
        {
            SettingsStore.ValidateProfile(value);
            if (ServiceConnected)
                await PipeClient.Call<bool>("apply", value.Fans);
            else if (value.Fans.Any(f => f.Enabled))
                throw new InvalidOperationException("The hardware service is unavailable; this profile has active cooling policies.");
            profile = value;
            history.Clear();
            Settings.ActiveProfileId = value.Id;
            ManualUntil = manual ? DateTimeOffset.UtcNow.AddMinutes(15) : null;
            Changed(nameof(Profile));
            ProfileChanged?.Invoke();
            Save();
        }
        finally { profileSwitch.Release(); }
    }
    public async Task Run(Func<Task> task)
    {
        try
        {
            await task();
        }
        catch (Exception e) { Status = e.Message; }
    }
    public void Save()
    {
        if (!settingsWritable)
            throw new InvalidOperationException("The existing settings file could not load. Export your current profile, then recover the settings backup before saving.");
        SettingsStore.ValidateProfile(profile);
        store.Save(Settings);
        Status = "Profile saved locally.";
    }
    public void SavePreferences()
    {
        SettingsStore.Validate(Settings);
        Save();
        StartupPreferences.Apply(Settings.StartWithWindows, Settings.StartInTray);
        Status = "Preferences saved. Startup and tray options apply on the next launch.";
    }
    public async Task CloseRgbApps()
    {
        await MkRgb.Hardware.Sensors.CloseRgbAppsAsync(life.Token);
        Status = "Competing RGB app windows were asked to close. Background services must be stopped through their own app.";
    }
    public void Record() => history.Record(Profile);
    public void Undo()
    {
        Replace(history.Undo(Profile));
        Status = "Undo.";
    }
    public void Redo()
    {
        Replace(history.Redo(Profile));
        Status = "Redo.";
    }
    void Replace(Profile value)
    {
        var i = Settings.Profiles.FindIndex(p => p.Id == profile.Id);
        Settings.Profiles[i] = value;
        profile = value;
        Changed("");
        ProfileChanged?.Invoke();
    }
    public void RefreshProfile()
    {
        Changed("");
        ProfileChanged?.Invoke();
    }
    public void NewProfile()
    {
        var p = Json.Clone(Profile);
        p.Id = Guid.NewGuid().ToString("N");
        p.Name = "Untitled " + (Settings.Profiles.Count + 1);
        foreach (var f in p.Fans)
            f.Enabled = false;
        Settings.Profiles.Add(p);
        Profile = p;
        Changed(nameof(Settings));
    }
    public void Import(string file)
    {
        var p = SettingsStore.Import(File.ReadAllText(file));
        Settings.Profiles.Add(p);
        Profile = p;
        Changed(nameof(Settings));
        Save();
    }
    public void Export(string file)
    {
        SettingsStore.ValidateProfile(Profile);
        File.WriteAllText(file, Json.Write(Profile));
        Status = "Profile exported.";
    }
    public void AddVirtual()
    {
        Record();
        Profile.Layout.Add(new()
        {
            DeviceId = "virtual:" + Guid.NewGuid().ToString("N"),
            Name = "Preview strip " + (Profile.Layout.Count + 1),
            X = 100 + Profile.Layout.Count * 30,
            Y = 100 + Profile.Layout.Count * 80
        });
        RefreshProfile();
    }
    public EffectContext Context() => new(clock.Elapsed.TotalSeconds, Snapshot.Sensors.Where(s => s.IsFresh(DateTimeOffset.UtcNow)).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.Last().Value!.Value), Inputs.Spectrum, Inputs.Screen, events.Where(e => DateTimeOffset.UtcNow - e.Value.At < TimeSpan.FromSeconds(15)).ToDictionary(e => e.Key, e => e.Value.Value));
    public async Task ConnectRgb()
    {
        await rgbConnection.WaitAsync(life.Token);
        try { await DiscoverRgb(); }
        finally { rgbConnection.Release(); }
    }
    async Task DiscoverRgb()
    {
        Status = "Starting RGB backend and scanning devices…";
        if (Settings.CloseRgbAppsOnConnect)
            await CloseRgbApps();
        // Check the pipe directly: the user can connect before the first monitor poll finishes.
        try
        {
            await PipeClient.Call<HardwareSnapshot>("snapshot", timeout: 1200, token: life.Token);
            ServiceConnected = true;
        }
        catch (OperationCanceledException) when (!life.IsCancellationRequested && !ServiceConnected)
        {
            // Portable mode can use an already running SDK server.
        }
        if (ServiceConnected)
            await PipeClient.Call<bool>("startRgb", timeout: 65000, token: life.Token);
        List<Device> devices;
        try { devices = await RgbSetup.RestoreAsync(Rgb, Settings.RgbZoneCounts, life.Token); }
        catch (System.Net.Sockets.SocketException e)
        {
            throw new IOException(ServiceConnected
                ? "OpenRGB SDK disconnected. Retry Connect RGB. " + e.Message
                : "RGB backend is offline. Install MK RGB with the Setup EXE to start it automatically, or start OpenRGB's SDK server on 127.0.0.1:6743.", e);
        }
        extensionRgb.Clear();
        foreach (var adapter in extensions.RgbControllers)
        {
            var prefix = "extension:" + adapter.GetType().Assembly.GetName().Name + ":" + adapter.GetType().FullName + ":";
            foreach (var d in await adapter.DiscoverAsync(life.Token))
            {
                var id = prefix + d.Id;
                if (!extensionRgb.TryAdd(id, (adapter, d.Id)))
                    throw new InvalidDataException("Duplicate extension device ID.");
                devices.Add(d with
                {
                    Id = id,
                    Verified = false
                });
            }
        }
        foreach (var old in Devices.Where(d => d.Capabilities.HasFlag(Capability.Rgb)).ToList())
            Devices.Remove(old);
        foreach (var d in devices)
        {
            Devices.Add(d);
            if (Profile.Layout.FirstOrDefault(p => p.DeviceId == d.Id) is { } existing)
            {
                existing.LedCount = Math.Max(1, d.LedCount);
                foreach (var index in existing.LedOverrides.Keys.Where(k => k >= existing.LedCount).ToList())
                    existing.LedOverrides.Remove(index);
            }
            else
                Profile.Layout.Add(new()
                {
                    DeviceId = d.Id,
                    Name = d.Name,
                    LedCount = Math.Max(1, d.LedCount),
                    X = 80,
                    Y = 60 + Profile.Layout.Count * 85
                });
        }
        Status = devices.Count == 0 ? "OpenRGB connected; no controllable RGB devices found." : $"Connected {devices.Count} RGB devices. Enable lighting output when ready.";
        Changed("");
        RefreshProfile();
    }
    async Task Poll()
    {
        var tick = 0;
        while (!life.IsCancellationRequested)
        {
            try
            {
                HardwareSnapshot data;
                try
                {
                    data = await PipeClient.Call<HardwareSnapshot>("snapshot", timeout: 1200, token: life.Token);
                    ServiceConnected = true;
                    fallback?.Dispose();
                    fallback = null;
                    Connection = "Hardware service connected";
                }
                catch (Exception) when (!life.IsCancellationRequested) { ServiceConnected = false; data = await Task.Run(() => { fallback ??= new(); return fallback.Read(); }, life.Token); Connection = "Monitoring only · service offline"; }
                Snapshot = data;
                telemetry.Append(data.Sensors);
                if (status == "Discovering hardware…")
                    Status = $"Monitoring {data.Sensors.Count} sensors. " + (ServiceConnected ? "Hardware service is ready." : "Install the hardware service for privileged access.");
                var ids = data.Sensors.Select(s => s.Id).ToHashSet();
                foreach (var row in Sensors.Where(r => !ids.Contains(r.Id)).ToList())
                    Sensors.Remove(row);
                foreach (var s in data.Sensors)
                {
                    var row = Sensors.FirstOrDefault(r => r.Id == s.Id);
                    if (row is null)
                    {
                        row = new(s);
                        Sensors.Add(row);
                    }
                    row.Update(s, Settings);
                }
                if (!Devices.Where(d => !d.Capabilities.HasFlag(Capability.Rgb)).Select(d => d.Id).SequenceEqual(data.Devices.Select(d => d.Id)))
                {
                    foreach (var old in Devices.Where(d => !d.Capabilities.HasFlag(Capability.Rgb)).ToList())
                        Devices.Remove(old);
                    foreach (var d in data.Devices)
                        Devices.Add(d);
                }
                if (!Fans.Select(f => f.Id).SequenceEqual(data.Fans.Select(f => f.Id)))
                {
                    Fans.Clear();
                    foreach (var f in data.Fans)
                        Fans.Add(f);
                }
                Changed(nameof(Cpu));
                Changed(nameof(Gpu));
                Changed(nameof(DeviceCount));
                Changed(nameof(SensorCount));
                foreach (var rule in Settings.Alerts.Where(r => r.Enabled))
                {
                    var s = data.Sensors.FirstOrDefault(s => s.Id == rule.SensorId);
                    if (s?.IsFresh(DateTimeOffset.UtcNow) == true && (s.Value > rule.Above || s.Value < rule.Below) && (!lastAlerts.TryGetValue(s.Id, out var last) || DateTimeOffset.UtcNow - last > TimeSpan.FromMinutes(1)))
                    {
                        lastAlerts[s.Id] = DateTimeOffset.UtcNow;
                        Alert?.Invoke($"{s.Name}: {s.Value:0.#} {s.Unit}");
                    }
                }
                if (tick % 3 == 0)
                    EvaluateAutomation();
                if (tick++ % 3600 == 0)
                    telemetry.Prune(Settings.RetentionDays);
            }
            catch (OperationCanceledException) when (life.IsCancellationRequested) { break; }
            catch (Exception e) { Status = "Monitoring: " + e.Message; }
            foreach (var sensor in Sensors)
                sensor.RefreshAvailability();
            Changed(nameof(Cpu));
            Changed(nameof(Gpu));
            try
            {
                await Task.Delay(1000, life.Token);
            }
            catch (OperationCanceledException) { break; }
        }
    }
    void EvaluateAutomation()
    {
        var processes = Process.GetProcesses().Select(p => { try { return p.ProcessName; } catch { return ""; } finally { p.Dispose(); } }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var power = System.Windows.Forms.SystemInformation.PowerStatus;
        var ctx = new AutomationContext(DateTimeOffset.Now, processes, Context().Sensors.ToDictionary(), power.BatteryLifePercent <= 1 ? power.BatteryLifePercent * 100 : -1, power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online, Context().Events.ToDictionary());
        var selected = Automations.Select(Settings.Rules, ctx, ManualUntil);
        if (selected is null && Settings.PlaylistEnabled && Settings.Playlist.Count > 0 && !(ManualUntil > DateTimeOffset.UtcNow))
            selected = Settings.Playlist[(int)(clock.Elapsed.TotalSeconds / Settings.PlaylistSeconds) % Settings.Playlist.Count];
        if (selected != null && selected != Profile.Id && Settings.Profiles.FirstOrDefault(p => p.Id == selected) is { } next)
            _ = Run(() => ActivateProfile(next, false));
    }
    async Task Render()
    {
        while (!life.IsCancellationRequested)
        {
            try
            {
                if (LightingEnabled)
                {
                    if (Rgb.NeedsDiscovery)
                        await ConnectRgb();
                    await rgbConnection.WaitAsync(life.Token);
                    try
                    {
                        if (LightingEnabled)
                        {
                            var context = Context();
                            var connected = Devices.Where(d => d.Capabilities.HasFlag(Capability.Rgb) && d.LedCount > 0).Select(d => d.Id).ToHashSet();
                            foreach (var d in Profile.Layout.Where(p => p.DeviceId.StartsWith("openrgb:") && connected.Contains(p.DeviceId)).ToList())
                                await Rgb.WriteAsync(d.DeviceId, Effects.Render(Profile, d, context), life.Token);
                            foreach (var d in Profile.Layout.Where(p => extensionRgb.ContainsKey(p.DeviceId)).ToList())
                            {
                                var adapter = extensionRgb[d.DeviceId];
                                await adapter.Controller.WriteAsync(adapter.Native, Effects.Render(Profile, d, context), life.Token);
                            }
                        }
                    }
                    finally { rgbConnection.Release(); }
                }
                if (System.Windows.Application.Current.MainWindow?.WindowState != WindowState.Minimized && System.Windows.Application.Current.MainWindow?.IsVisible == true)
                    Frame?.Invoke();
            }
            catch (Exception e) { LightingEnabled = false; Status = "Lighting paused: " + e.Message; Changed(nameof(LightingEnabled)); }
            try
            {
                await Task.Delay(1000 / Math.Clamp(Settings.Fps, 1, 60), life.Token);
            }
            catch (OperationCanceledException) { break; }
        }
    }
    public List<(DateTimeOffset At, double Value)> History(string id, int hours = 1) => telemetry.Read(id, DateTimeOffset.UtcNow.AddHours(-hours));
    public void ExportTelemetry(string path)
    {
        telemetry.Export(path);
        Status = "Telemetry exported.";
    }
    public async Task ApplyCooling()
    {
        Save();
        await PipeClient.Call<bool>("apply", Profile.Fans);
        Status = "Cooling policies applied to the persistent service.";
    }
    public async Task RestoreCooling()
    {
        await PipeClient.Call<bool>("restore");
        foreach (var p in Profile.Fans)
            p.Enabled = false;
        Save();
        Status = "Firmware/default fan control restored.";
    }
    public async Task RestartApi()
    {
        await Api.DisposeAsync();
        if (Settings.ApiEnabled)
            await Api.Start(Settings, (name, value) => dispatcher.Invoke(() => { if (name == "health" && events.TryGetValue(name, out var previous)) events["damage"] = (Math.Max(0, previous.Value - value), DateTimeOffset.UtcNow); events[name] = (value, DateTimeOffset.UtcNow); }), id => dispatcher.InvokeAsync(() => ActivateProfile(Settings.Profiles.First(p => p.Id == id), true)).Task.Unwrap());
        Save();
        Status = Settings.ApiEnabled ? $"Local API listening on 127.0.0.1:{Settings.ApiPort}." : "Local API disabled.";
    }
    public async Task LoadCatalog()
    {
        var cache = Path.Combine(Paths.UserData, "catalog.json");
        string data;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MK-RGB/0.1");
            data = await http.GetStringAsync("https://raw.githubusercontent.com/MKonline08/mk-rgb/main/catalog/index.json");
            _ = Json.Read<List<CatalogItem>>(data);
            SettingsStore.AtomicWrite(cache, data);
        }
        catch { data = File.Exists(cache) ? File.ReadAllText(cache) : File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog", "index.json")); }
        Catalog.Clear();
        foreach (var item in Json.Read<List<CatalogItem>>(data))
            Catalog.Add(item);
    }
    public async Task InstallCatalog(CatalogItem item)
    {
        if (item.Kind != "Profile")
            throw new InvalidOperationException("Compiled extensions require an administrator-reviewed installation. See the extension guide.");
        if (item.File.Contains("..") || item.File.StartsWith('/') || item.File.Contains(':'))
            throw new InvalidDataException("Invalid catalog path.");
        var local = Path.Combine(AppContext.BaseDirectory, "catalog", item.File);
        byte[] bytes;
        if (File.Exists(local))
            bytes = await File.ReadAllBytesAsync(local);
        else
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            bytes = await client.GetByteArrayAsync("https://raw.githubusercontent.com/MKonline08/mk-rgb/main/catalog/" + item.File);
        }
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Catalog checksum mismatch.");
        var p = SettingsStore.Import(System.Text.Encoding.UTF8.GetString(bytes));
        Settings.Profiles.Add(p);
        Settings.InstalledCatalogVersions[item.Id] = item.Version;
        Profile = p;
        Changed(nameof(Settings));
        Save();
    }
    public async Task CheckUpdates()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MK-RGB/0.1.2");
        using var response = await http.GetAsync("https://api.github.com/repos/MKonline08/mk-rgb/releases/latest");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Status = "No stable release is published yet. This is a development preview.";
            return;
        }
        response.EnsureSuccessStatusCode();
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tag = json.RootElement.GetProperty("tag_name").GetString() ?? "";
        Status = Version.TryParse(tag.TrimStart('v'), out var version) && version > typeof(MainViewModel).Assembly.GetName().Version
            ? $"Update {tag} is available. Open Releases to review and install it."
            : "You are running the latest published version.";
    }
    public void Diagnostics(string path)
    {
        var report = new
        {
            version = typeof(MainViewModel).Assembly.GetName().Version?.ToString(),
            os = Environment.OSVersion.VersionString,
            service = ServiceConnected,
            devices = Devices.Select(d => new { d.Name, d.Vendor, d.Capabilities, d.Verified }),
            sensors = Snapshot.Sensors.Select(s => new { s.Name, s.Kind, s.Value, s.Unit }),
            conflicts = MkRgb.Hardware.Sensors.Conflicts(),
            warnings = Snapshot.Warnings.Select(w => w.Replace(Environment.UserName, "[user]").Replace(Paths.UserData, "[data]"))
        };
        File.WriteAllText(path, Json.Write(report));
        Status = "Diagnostic report saved. Review before sharing.";
    }
    public async ValueTask DisposeAsync()
    {
        life.Cancel();
        if (polling != null)
            await polling;
        if (rendering != null)
            await rendering;
        Inputs.Dispose();
        await Api.DisposeAsync();
        await Rgb.DisposeAsync();
        foreach (var (source, handler) in eventHandlers)
            source.EventReceived -= handler;
        foreach (var adapter in extensions.RgbControllers)
            await adapter.DisposeAsync();
        extensions.Dispose();
        fallback?.Dispose();
        telemetry.Dispose();
        life.Dispose();
    }
}
