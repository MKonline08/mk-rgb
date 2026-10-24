using System.Text.Json;
using System.Text.Json.Serialization;

namespace MkRgb.Core;

[Flags]
public enum Capability
{
    None = 0, Monitor = 1, Rgb = 2, Fan = 4, Virtual = 8
}
public enum EffectKind
{
    Static, Breathing, Pulse, Rainbow, Wave, Chase, Gradient, Sparkle, Ripple, Temperature, FanSpeed, Audio, Screen, Game, Timeline, Graph
}
public enum SensorKind
{
    Temperature, Load, Clock, Power, Voltage, Fan, Control, Other
}
public enum BlendMode
{
    Normal, Add, Multiply, Screen
}
public enum TriggerKind
{
    Application, Schedule, Temperature, Battery, AcPower, Event
}
public enum MixMode
{
    Maximum, Average
}
public record Rgb(byte R, byte G, byte B)
{
    public static Rgb Black => new(0, 0, 0);
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    public static Rgb Parse(string value)
    {
        var s = value.Trim().TrimStart('#');
        return s.Length == 6 && int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var n) ? new((byte)(n >> 16), (byte)(n >> 8), (byte)n) : throw new FormatException("Use a six-digit color such as #56D9C0.");
    }
    public Rgb Scale(double v) => new((byte)Math.Clamp(R * v, 0, 255), (byte)Math.Clamp(G * v, 0, 255), (byte)Math.Clamp(B * v, 0, 255));
    public static Rgb Lerp(Rgb a, Rgb b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }
    public static Rgb Hsv(double hue, double sat = 1, double val = 1)
    {
        hue = (hue % 1 + 1) % 1 * 6;
        var c = val * sat;
        var x = c * (1 - Math.Abs(hue % 2 - 1));
        var m = val - c;
        var (r, g, b) = (int)hue switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x)
        };
        return new((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
public record Zone(string Id, string Name, int Start, int Count, bool Resizable = false);
public record Device(string Id, string Name, string Vendor, Capability Capabilities, int LedCount, List<Zone> Zones, string Status = "Available", bool Verified = false);
public record SensorSample(string Id, string Name, SensorKind Kind, double? Value, string Unit, DateTimeOffset At, string DeviceId)
{
    public bool IsFresh(DateTimeOffset now) => Value is { } v && double.IsFinite(v) && now >= At && now - At < TimeSpan.FromSeconds(5);
}
public record FanChannel(string Id, string Name, string DeviceId, double Min, double Max, bool Writable, bool CanRestore, bool Pump, string Evidence, string? RpmSensorId = null);
public record HardwareSnapshot(List<Device> Devices, List<SensorSample> Sensors, List<FanChannel> Fans, List<string> Warnings, DateTimeOffset At);
public record Placement
{
    public string DeviceId { get; set; } = ""; public string Name { get; set; } = "";
    public double X { get; set; } = 100; public double Y { get; set; } = 100;
    public double Width { get; set; } = 180; public double Height { get; set; } = 60; public double Rotation
    {
        get; set;
    }
    public string Group { get; set; } = "All"; public int LedCount { get; set; } = 16; public string ColorOrder { get; set; } = "RGB";
    public Dictionary<int, string> LedOverrides { get; set; } = [];
}
public record Keyframe
{
    public double Time
    {
        get; set;
    }
    public string Color { get; set; } = "#67E8C5"; public bool Hold
    {
        get; set;
    }
}
public record EffectNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8]; public string Type { get; set; } = "Color";
    public List<string> Inputs { get; set; } = []; public double Value { get; set; } = 1; public string Color { get; set; } = "#67E8C5";
    public string Source { get; set; } = ""; public double X { get; set; } = -1; public double Y { get; set; } = -1;
}
public record EffectLayer
{
    public string Name { get; set; } = "Base"; public EffectKind Kind { get; set; } = EffectKind.Wave;
    public string Color { get; set; } = "#67E8C5"; public string Color2 { get; set; } = "#707BFF";
    public double Speed { get; set; } = 0.25; public double Brightness { get; set; } = 0.7; public double Direction
    {
        get; set;
    }
    public double Opacity { get; set; } = 1; public BlendMode Blend
    {
        get; set;
    }
    public string Group { get; set; } = "All"; public string SensorId { get; set; } = "";
    public double MaskStart { get; set; } = 0; public double MaskEnd { get; set; } = 1;
    public List<Keyframe> Keyframes { get; set; } = [new() { Time = 0 }, new() { Time = 2, Color = "#707BFF" }, new() { Time = 4 }];
    public List<EffectNode> Nodes { get; set; } = [new() { Id = "base", Type = "Color" }]; public string OutputNode { get; set; } = "base";
}
public record CurvePoint(double Temperature, double Duty);
public record CurveSource
{
    public string SensorId { get; set; } = ""; public List<CurvePoint> Points { get; set; } = [new(30, 30), new(50, 40), new(70, 70), new(85, 100)];
}
public record FanPolicy
{
    public string ChannelId { get; set; } = ""; public bool Enabled
    {
        get; set;
    }
    public bool Manual
    {
        get; set;
    }
    public double ManualDuty { get; set; } = 60;
    public List<CurveSource> Sources { get; set; } = [new()]; public MixMode Mix
    {
        get; set;
    }
    public bool MixTemperatures
    {
        get; set;
    }
    public double Minimum { get; set; } = 30; public double StartDuty { get; set; } = 40;
    public double RisePerSecond { get; set; } = 20; public double FallPerSecond { get; set; } = 5;
    public double Hysteresis { get; set; } = 2; public bool AllowStop
    {
        get; set;
    }
    public bool Calibrated
    {
        get; set;
    }
    public bool Pump
    {
        get; set;
    }
}
public record Profile
{
    public int Version { get; set; } = 1; public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Studio"; public List<EffectLayer> Layers { get; set; } = [new()];
    public List<Placement> Layout { get; set; } = []; public List<FanPolicy> Fans { get; set; } = [];
}
public record AutomationRule
{
    public string Name { get; set; } = "New rule"; public bool Enabled { get; set; } = true;
    public TriggerKind Trigger
    {
        get; set;
    }
    public string Source { get; set; } = ""; public double Threshold { get; set; } = 70;
    public string Start { get; set; } = "18:00"; public string End { get; set; } = "23:00";
    public string ProfileId { get; set; } = ""; public int Priority { get; set; } = 10;
}
public record AlertRule
{
    public string SensorId { get; set; } = ""; public double Above { get; set; } = 85; public double? Below
    {
        get; set;
    }
    public bool Enabled { get; set; } = true;
}
public record Settings
{
    public int Version { get; set; } = 1; public string ActiveProfileId { get; set; } = "";
    public List<Profile> Profiles { get; set; } = [new()]; public List<AutomationRule> Rules { get; set; } = [];
    public List<AlertRule> Alerts { get; set; } = []; public Dictionary<string, string> SensorNames { get; set; } = [];
    public List<string> PinnedSensors { get; set; } = [];
    public Dictionary<string, string> InstalledCatalogVersions { get; set; } = [];
    public bool Fahrenheit
    {
        get; set;
    }
    public bool ReducedMotion
    {
        get; set;
    }
    public bool ApiEnabled
    {
        get; set;
    }
    public int Fps { get; set; } = 30; public int RetentionDays { get; set; } = 7;
    public List<string> Playlist { get; set; } = []; public int PlaylistSeconds { get; set; } = 30;
    public bool PlaylistEnabled
    {
        get; set;
    }
    public int ApiPort { get; set; } = 19420;
    public string ApiToken { get; set; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
}
public record EffectContext(double Time, IReadOnlyDictionary<string, double> Sensors, float[] Audio, Rgb Screen, IReadOnlyDictionary<string, double> Events);
public static class Json
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Options) ?? throw new InvalidDataException("Empty document.");
    public static T Clone<T>(T value) => Read<T>(Write(value));
}
public interface ISensorProvider
{
    HardwareSnapshot Read();
}
public interface IFanController
{
    void SetDuty(string id, double duty); void Restore(string id);
}
public interface IRgbController : IAsyncDisposable
{
    Task<List<Device>> DiscoverAsync(CancellationToken token); Task WriteAsync(string id, Rgb[] colors, CancellationToken token);
}
public interface IEventSource
{
    event Action<string, double>? EventReceived;
}
public interface IFanWatchdog
{
    void ArmFallback(string id, TimeSpan timeout, double duty);
}
