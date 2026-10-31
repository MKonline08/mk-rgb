using Microsoft.Data.Sqlite;
using System.Globalization;

namespace MkRgb.Core;

public static class Paths
{
    public static string UserData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MK RGB");
    public static string MachineData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MK RGB");
}
public sealed class SettingsStore(string directory)
{
    public string FilePath => Path.Combine(directory, "settings.json");
    public Settings Load()
    {
        if (!File.Exists(FilePath))
            return new();
        var data = Json.Read<Settings>(File.ReadAllText(FilePath));
        Validate(data);
        return data;
    }
    public void Save(Settings settings)
    {
        Validate(settings);
        Directory.CreateDirectory(directory);
        AtomicWrite(FilePath, Json.Write(settings));
    }
    public static void AtomicWrite(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content);
            if (File.Exists(path))
                File.Copy(path, path + ".bak", true);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void Validate(Settings s)
    {
        if (s.Version != 1)
            throw new InvalidDataException("This settings version is not supported. Keep the backup and use the matching MK RGB version.");
        if (s.Profiles.Count == 0 || s.Profiles.Select(p => p.Id).Distinct().Count() != s.Profiles.Count)
            throw new InvalidDataException("Profiles need unique IDs.");
        if (s.Fps is < 1 or > 60 || s.RetentionDays is < 1 or > 90 || s.PlaylistSeconds < 5 || s.ApiPort is < 1024 or > 65535)
            throw new InvalidDataException("Invalid performance, retention, playlist, or API settings.");
        foreach (var p in s.Profiles)
            ValidateProfile(p);
        if (s.RgbZoneCounts.Count > 256 || s.RgbZoneCounts.Any(d => d.Value.Count > 256 || d.Value.Any(z => !int.TryParse(z.Key, out var index) || index < 0 || z.Value is < 0 or > 10000)))
            throw new InvalidDataException("Invalid saved RGB header configuration.");
    }
    public static void ValidateProfile(Profile p)
    {
        if (p.Version != 1 || string.IsNullOrWhiteSpace(p.Name) || p.Layers.Count > 32 || p.Layout.Count > 128)
            throw new InvalidDataException("Invalid profile version, name, or size.");
        foreach (var e in p.Layers)
        {
            _ = Rgb.Parse(e.Color);
            _ = Rgb.Parse(e.Color2);
            if (!double.IsFinite(e.Speed) || !double.IsFinite(e.Direction) || e.Brightness is < 0 or > 1 || !double.IsFinite(e.Brightness) || e.Opacity is < 0 or > 1 || !double.IsFinite(e.Opacity) || e.MaskStart < 0 || e.MaskEnd > 1 || e.MaskStart > e.MaskEnd)
                throw new InvalidDataException("Invalid layer parameters.");
            foreach (var k in e.Keyframes)
            {
                _ = Rgb.Parse(k.Color);
                if (!double.IsFinite(k.Time) || k.Time < 0)
                    throw new InvalidDataException("Keyframe times must be positive.");
            }
            if (e.Keyframes.Select(k => k.Time).Distinct().Count() != e.Keyframes.Count)
                throw new InvalidDataException("Keyframe times must be unique.");
            if (e.Kind == EffectKind.Graph)
                Effects.ValidateGraph(e);
        }
        foreach (var d in p.Layout)
        {
            if (d.Lighting is { } quick)
                ValidateProfile(new Profile { Layers = [quick], Layout = [], Fans = [] });
            if (d.LedCount is < 1 or > 10000 || !double.IsFinite(d.X) || !double.IsFinite(d.Y) || !double.IsFinite(d.Width) || !double.IsFinite(d.Height) || !double.IsFinite(d.Rotation) || d.Width <= 0 || d.Height <= 0 || !new[] { "RGB", "GRB", "BGR", "BRG", "RBG", "GBR" }.Contains(d.ColorOrder))
                throw new InvalidDataException("Invalid device layout.");
            foreach (var led in d.LedOverrides)
            {
                if (led.Key < 0 || led.Key >= d.LedCount)
                    throw new InvalidDataException("LED index is out of range.");
                _ = Rgb.Parse(led.Value);
            }
        }
        foreach (var f in p.Fans)
        {
            if (!double.IsFinite(f.ManualDuty) || f.ManualDuty is < 0 or > 100 || !double.IsFinite(f.Minimum) || f.Minimum is < 0 or > 100 || f.StartDuty is < 0 or > 100 || !double.IsFinite(f.StartDuty) || !double.IsFinite(f.Hysteresis) || f.Hysteresis < 0 || !double.IsFinite(f.RisePerSecond) || f.RisePerSecond <= 0 || !double.IsFinite(f.FallPerSecond) || f.FallPerSecond <= 0)
                throw new InvalidDataException("Invalid cooling parameters.");
            foreach (var source in f.Sources)
                _ = CoolingEngine.Interpolate(source.Points, 50);
        }
    }
    public static Profile Import(string json)
    {
        if (json.Length > 4_000_000)
            throw new InvalidDataException("Profile exceeds 4 MB.");
        var p = Json.Read<Profile>(json);
        ValidateProfile(p);
        p.Id = Guid.NewGuid().ToString("N");
        foreach (var f in p.Fans)
        {
            f.Enabled = false;
            f.Calibrated = false;
            f.AllowStop = false;
            f.ChannelId = "";
            foreach (var s in f.Sources)
                s.SensorId = "";
        }
        foreach (var d in p.Layout)
        {
            d.DeviceId = "virtual:" + Guid.NewGuid().ToString("N");
        }
        return p;
    }
}
public sealed class TelemetryStore : IDisposable
{
    readonly SqliteConnection db;
    public TelemetryStore(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        db = new($"Data Source={file}");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS samples (sensor TEXT NOT NULL, at INTEGER NOT NULL, value REAL NOT NULL, PRIMARY KEY(sensor,at)); CREATE INDEX IF NOT EXISTS by_time ON samples(at);";
        cmd.ExecuteNonQuery();
    }
    public void Append(IEnumerable<SensorSample> samples)
    {
        using var tx = db.BeginTransaction();
        foreach (var s in samples.Where(s => s.Value is { } v && double.IsFinite(v)))
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR REPLACE INTO samples VALUES($id,$at,$value)";
            cmd.Parameters.AddWithValue("$id", s.Id);
            cmd.Parameters.AddWithValue("$at", s.At.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$value", s.Value!);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
    public List<(DateTimeOffset At, double Value)> Read(string id, DateTimeOffset since, int limit = 10000)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT MIN(at),AVG(value) FROM samples WHERE sensor=$id AND at>=$since GROUP BY CAST((at-$since)/$bucket AS INTEGER) ORDER BY MIN(at) LIMIT $limit";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$since", since.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$bucket", Math.Max(1L, (long)Math.Ceiling((DateTimeOffset.UtcNow - since).TotalMilliseconds / Math.Max(1, limit))));
        using var r = cmd.ExecuteReader();
        var rows = new List<(DateTimeOffset, double)>();
        while (r.Read())
            rows.Add((DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(0)), r.GetDouble(1)));
        return rows;
    }
    public void Prune(int days)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM samples WHERE at<$time";
        cmd.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeMilliseconds());
        cmd.ExecuteNonQuery();
    }
    public void Export(string file)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT sensor,at,value FROM samples ORDER BY at";
        using var r = cmd.ExecuteReader();
        using var writer = new StreamWriter(file);
        writer.WriteLine("sensor,time_utc,value");
        while (r.Read())
            writer.WriteLine($"\"{r.GetString(0).Replace("\"", "\"\"")}\",{DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(1)):O},{r.GetDouble(2).ToString(CultureInfo.InvariantCulture)}");
    }
    public void Dispose()
    {
        SqliteConnection.ClearPool(db);
        db.Dispose();
    }
}
public record RpcRequest(string Method, string Payload = "", int Version = 1);
public record RpcResponse(bool Success, string Payload, string? Error = null, int Version = 1);
public record CalibrationRequest(string ChannelId, string RpmSensorId, string TemperatureSensorId, bool IsPump, bool Confirmed, bool TestStop = false);
public record CalibrationResult(string ChannelId, double Minimum, double StartDuty, bool CanStop, DateTimeOffset At, string RpmSensorId, string TemperatureSensorId, bool Pump);
public record ExtensionManifest(string Id, string Name, string Version, string Assembly, List<string> Capabilities, string Author, string License);
public record CatalogItem(string Id, string Name, string Description, string Kind, string Version, string File, string Sha256, string[] Tags, string[]? PreviewColors = null, string Compatibility = "Map virtual devices to your hardware after import.");
