using MkRgb.Core;
using MkRgb.Hardware;
using MkRgb.Desktop;

namespace MkRgb.Tests;

public class LightingTests
{
    static readonly EffectContext context = new(0, new Dictionary<string, double>(), [], Rgb.Black, new Dictionary<string, double>());
    [Fact]
    public void OneColorReplacesPaintedLedsOnEverySelectedDeviceAndSurvivesSaving()
    {
        var profile = new Profile
        {
            Layers = [new() { Kind = EffectKind.Static, Color = "#0000FF", Brightness = 1 }],
            Layout = [new() { DeviceId = "gpu", LedCount = 4, LedOverrides = new() { [2] = "#00FF00" } }, new() { DeviceId = "mouse", LedCount = 10 }]
        };
        Assert.Equal(2, QuickLighting.Apply(profile, ["gpu", "mouse"], EffectKind.Static, "#FF0000", .5, .25));
        var loaded = Json.Read<Profile>(Json.Write(profile));
        SettingsStore.ValidateProfile(loaded);
        foreach (var device in loaded.Layout)
        {
            Assert.Empty(device.LedOverrides);
            Assert.All(Effects.Render(loaded, device, context), color => Assert.Equal(new Rgb(127, 0, 0), color));
        }
    }
    [Fact]
    public void ADeviceCanUseItsOwnColorWithoutChangingOtherDevicesOrAdvancedLayers()
    {
        var profile = new Profile { Layers = [new() { Kind = EffectKind.Static, Color = "#0000FF", Brightness = 1 }], Layout = [new() { DeviceId = "gpu" }, new() { DeviceId = "mouse" }] };
        QuickLighting.Apply(profile, ["mouse"], EffectKind.Static, "#FF0000", 1, 1);
        Assert.All(Effects.Render(profile, profile.Layout[0], context), c => Assert.Equal(new Rgb(0, 0, 255), c));
        Assert.All(Effects.Render(profile, profile.Layout[1], context), c => Assert.Equal(new Rgb(255, 0, 0), c));
        profile.Layout[1].Lighting = null;
        Assert.All(Effects.Render(profile, profile.Layout[1], context), c => Assert.Equal(new Rgb(0, 0, 255), c));
    }
    [Fact]
    public void InvalidQuickColorDoesNotEraseExistingPaintOrComposition()
    {
        var profile = new Profile { Layout = [new() { DeviceId = "mouse", LedOverrides = new() { [0] = "#FFFFFF" } }] };
        var before = Json.Write(profile);
        Assert.Throws<FormatException>(() => QuickLighting.Apply(profile, ["mouse"], EffectKind.Static, "purple", 1, 1));
        Assert.Equal(before, Json.Write(profile));
        Assert.Throws<InvalidDataException>(() => QuickLighting.Apply(profile, ["mouse"], EffectKind.Static, "#FF0000", double.NaN, 1));
        Assert.Equal(before, Json.Write(profile));
    }
    [Fact]
    public void ComponentQuantityUsesReportedHeaderLimits()
    {
        var zone = new Zone("1", "Header", 0, 0, true, 0, 120);
        Assert.Equal(36, QuickLighting.ComponentLeds(12, 3, zone));
        Assert.Throws<InvalidDataException>(() => QuickLighting.ComponentLeds(12, 11, zone));
        Assert.Throws<InvalidDataException>(() => QuickLighting.ComponentLeds(0, 3, zone));
        Assert.Throws<InvalidDataException>(() => QuickLighting.ComponentLeds(12, 3, zone with { Resizable = false }));
    }
    [Fact]
    public void ColorWheelHasWhiteAtItsCenterAndRedAtItsRightEdge()
    {
        Assert.Equal(new Rgb(255, 255, 255), ColorWheel.At(0, 0));
        Assert.Equal(new Rgb(255, 0, 0), ColorWheel.At(1, 0));
        Assert.Equal(new Rgb(0, 255, 255), ColorWheel.At(-1, 0));
    }
    [Fact]
    public async Task MultipleHeaderConfigurationsRestoreAfterBackendRestart()
    {
        var settings = Json.Read<Settings>(Json.Write(new Settings { RgbZoneCounts = new() { ["board"] = new() { ["1"] = 36, ["2"] = 16 }, ["unplugged"] = new() { ["0"] = 24 } } }));
        SettingsStore.Validate(settings);
        await using var backend = new RestartedBackend();
        var devices = await RgbSetup.RestoreAsync(backend, settings.RgbZoneCounts, CancellationToken.None);
        Assert.Equal(new[] { 36, 16 }, devices[0].Zones.Select(z => z.Count));
        Assert.Equal(2, backend.Resizes);
        await RgbSetup.RestoreAsync(backend, settings.RgbZoneCounts, CancellationToken.None);
        Assert.Equal(2, backend.Resizes); // Already configured headers are not rewritten.
        Assert.True(settings.RgbZoneCounts.ContainsKey("unplugged"));
    }
    [Fact]
    public async Task ARejectedHeaderResizeIsReportedInsteadOfClaimingItWasRestored()
    {
        await using var backend = new RestartedBackend { Reject = true };
        await Assert.ThrowsAsync<IOException>(() => RgbSetup.RestoreAsync(backend, new() { ["board"] = new() { ["1"] = 36 } }, CancellationToken.None));
    }
    [Fact]
    public void OldSettingsNeedNoMigrationAndInvalidHeaderCountsAreRejected()
    {
        var old = Json.Read<Settings>("{\"Version\":1}");
        SettingsStore.Validate(old);
        Assert.Empty(old.RgbZoneCounts);
        old.RgbZoneCounts["board"] = new() { ["1"] = -1 };
        Assert.Throws<InvalidDataException>(() => SettingsStore.Validate(old));
    }
    [Fact]
    public void UnusedHeadersDoNotKeepShowingSetupWarnings()
    {
        var empty = new Zone("1", "ARGB", 0, 0, true);
        Dictionary<string, Dictionary<string, int>> saved = [];
        Assert.True(RgbSetup.NeedsSetup("board", empty, saved));
        saved["board"] = new() { ["1"] = 0 };
        Assert.False(RgbSetup.NeedsSetup("board", empty, saved));
        Assert.True(RgbSetup.NeedsSetup("different board", empty, saved));
        saved["board"]["1"] = 24;
        Assert.True(RgbSetup.NeedsSetup("board", empty, saved));
    }
    sealed class RestartedBackend : IResizableRgbController
    {
        List<Zone> zones = [new("1", "ARGB 1", 0, 0, true, 0, 120), new("2", "ARGB 2", 0, 0, true, 0, 120)];
        bool stale;
        public bool Reject { get; init; }
        public int Resizes { get; private set; }
        public Task<List<Device>> DiscoverAsync(CancellationToken token)
        {
            stale = false;
            return Task.FromResult<List<Device>>([new("board", "Motherboard", "Test", Capability.Rgb, zones.Sum(z => z.Count), zones.ToList())]);
        }
        public Task ResizeAsync(string id, int zone, int count, CancellationToken token)
        {
            if (stale) throw new IOException("Inventory must refresh after every resize.");
            stale = true; Resizes++;
            if (!Reject) zones = zones.Select(z => z.Id == zone.ToString() ? z with { Count = count } : z).ToList();
            return Task.CompletedTask;
        }
        public Task WriteAsync(string id, Rgb[] colors, CancellationToken token) => throw new InvalidOperationException("Restoring configuration must not send lighting frames.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
