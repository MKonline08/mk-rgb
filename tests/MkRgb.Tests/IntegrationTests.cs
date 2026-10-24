using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using MkRgb.Core;
using MkRgb.Desktop;
using MkRgb.Hardware;

namespace MkRgb.Tests;

public class IntegrationTests
{
    [Fact]
    public void HistoricalChartCoversEntireRequestedRangeWhenDownsampled()
    {
        var root = Path.Combine(Path.GetTempPath(), "mkrgb-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new TelemetryStore(Path.Combine(root, "history.db"));
            var now = DateTimeOffset.UtcNow;
            store.Append(new[] { 23, 19, 15, 11, 7, 3 }.Select(hours => new SensorSample("cpu", "CPU", SensorKind.Temperature, 40 + hours, "°C", now.AddHours(-hours), "cpu")));
            var data = store.Read("cpu", now.AddHours(-24), 3);
            Assert.Equal(3, data.Count);
            Assert.True(data[0].At < now.AddHours(-20));
            Assert.True(data[^1].At > now.AddHours(-8));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void BundledCatalogChecksumsMatchPortableProfiles()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "catalog");
        var items = Json.Read<List<CatalogItem>>(File.ReadAllText(Path.Combine(directory, "index.json")));
        Assert.NotEmpty(items);
        foreach (var item in items)
        {
            var bytes = File.ReadAllBytes(Path.Combine(directory, item.File));
            Assert.Equal(item.Sha256.ToUpperInvariant(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
            SettingsStore.ValidateProfile(SettingsStore.Import(System.Text.Encoding.UTF8.GetString(bytes)));
        }
    }
    [Fact]
    public void ScalarSignalsRequireTypedColorConversion()
    {
        var layer = new EffectLayer { Kind = EffectKind.Graph, OutputNode = "output", Nodes = [new() { Id = "signal", Type = "Sensor", Source = "cpu", Value = 100 }, new() { Id = "output", Type = "Colorize", Inputs = ["signal"], Color = "#FF0000" }] };
        Effects.ValidateGraph(layer);
        Assert.Equal(new Rgb(127, 0, 0), Effects.Evaluate(layer, 0, 0, new(0, new Dictionary<string, double> { ["cpu"] = 50 }, [], Rgb.Black, new Dictionary<string, double>())));
        layer.Nodes[1].Type = "Transform";
        Assert.Throws<InvalidDataException>(() => Effects.ValidateGraph(layer));
    }
    [Fact]
    public async Task LocalApiRequiresTokenAndDoesNotExposeHardwareWrites()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var settings = new Settings { ApiPort = port };
        string? eventName = null;
        double eventValue = 0;
        string? profile = null;
        await using var api = new EventServer();
        await api.Start(settings, (name, value) => { eventName = name; eventValue = value; }, id => { profile = id; return Task.CompletedTask; });
        using var client = new HttpClient { BaseAddress = new($"http://127.0.0.1:{port}") };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/events", new
        {
            name = "health",
            value = 80
        })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", settings.ApiToken);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/v1/events", new
        {
            name = "health",
            value = 80
        })).StatusCode);
        Assert.Equal("health", eventName);
        Assert.Equal(80, eventValue);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/v1/fans/test", new
        {
            duty = 0
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/v1/profiles/" + settings.Profiles[0].Id, null)).StatusCode);
        Assert.Equal(settings.Profiles[0].Id, profile);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/v1/profiles/missing", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/v1/events", new StringContent("{broken", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/cs2", new
        {
            auth = new
            {
                token = "wrong"
            }
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/v1/cs2", new
        {
            auth = new
            {
                token = settings.ApiToken
            },
            player = new
            {
                state = new
                {
                    health = 42
                }
            }
        })).StatusCode);
        Assert.Equal(42, eventValue);
    }
    [Fact]
    public void SettingsAreAtomicAndKeepPreviousVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "mkrgb-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(root);
            var s = new Settings();
            store.Save(s);
            s.Profiles[0].Name = "Revision";
            store.Save(s);
            Assert.Equal("Revision", store.Load().Profiles[0].Name);
            Assert.Equal("Studio", Json.Read<Settings>(File.ReadAllText(store.FilePath + ".bak")).Profiles[0].Name);
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact] public void NonFiniteSensorIsNeverFresh() => Assert.False(new SensorSample("x", "x", SensorKind.Temperature, double.NaN, "°C", DateTimeOffset.UtcNow, "x").IsFresh(DateTimeOffset.UtcNow));
    [Fact]
    public void ExplicitLedColorOverridesOnlyThatLed()
    {
        var p = new Placement { LedCount = 3, LedOverrides = new() { { 1, "#FF0000" } } };
        var frame = Effects.Render(new()
        {
            Layers = [new() { Kind = EffectKind.Static, Color = "#0000FF", Brightness = 1 }]
        }, p, new(0, new Dictionary<string, double>(), [], Rgb.Black, new Dictionary<string, double>()));
        Assert.Equal(new Rgb(0, 0, 255), frame[0]);
        Assert.Equal(new Rgb(255, 0, 0), frame[1]);
        Assert.Equal(new Rgb(0, 0, 255), frame[2]);
    }
}
