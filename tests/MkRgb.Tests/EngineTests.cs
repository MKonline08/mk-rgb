using MkRgb.Core;
using MkRgb.Hardware;
using System.Text;

namespace MkRgb.Tests;

public class EngineTests
{
    static EffectContext Context(double t = 0) => new(t, new Dictionary<string, double> { { "cpu", 55 } }, [.5f], new(20, 40, 60), new Dictionary<string, double> { { "health", 75 } });
    static SensorSample Temperature(double? t, DateTimeOffset now) => new("cpu", "CPU", SensorKind.Temperature, t, "°C", now, "test");
    [Fact]
    public void ValidatedStopDoesNotLingerBelowMinimumRunningDuty()
    {
        var now = DateTimeOffset.UtcNow;
        var p = new FanPolicy { ChannelId = "fan", AllowStop = true, Calibrated = true, Hysteresis = 0, Minimum = 30, StartDuty = 45, FallPerSecond = 10, Sources = [new() { SensorId = "cpu", Points = [new(30, 0), new(50, 40), new(85, 100)] }] };
        var engine = new CoolingEngine();
        Assert.Equal(45, engine.Evaluate(p, [Temperature(50, now)], now));
        Assert.Equal(35, engine.Evaluate(p, [Temperature(30, now.AddSeconds(1))], now.AddSeconds(1)));
        Assert.Equal(0, engine.Evaluate(p, [Temperature(30, now.AddSeconds(2))], now.AddSeconds(2)));
        Assert.Equal(45, engine.Evaluate(p, [Temperature(50, now.AddSeconds(3))], now.AddSeconds(3)));
    }
    [Fact]
    public void PumpNeverStopsEvenWithImportedStopFlag()
    {
        var now = DateTimeOffset.UtcNow;
        var p = new FanPolicy { Pump = true, AllowStop = true, Calibrated = true, Minimum = 70, StartDuty = 85, Sources = [new() { SensorId = "cpu", Points = [new(30, 0), new(85, 100)] }] };
        Assert.Equal(85, new CoolingEngine().Evaluate(p, [Temperature(20, now)], now));
    }
    [Fact]
    public void FutureSensorTimestampTriggersFallback()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(100, new CoolingEngine().Evaluate(new()
        {
            Sources = [new() { SensorId = "cpu" }]
        }, [Temperature(30, now.AddSeconds(5))], now));
    }
    [Theory]
    [InlineData(20, 30)]
    [InlineData(40, 40)]
    [InlineData(60, 60)]
    [InlineData(90, 100)]
    public void CurveInterpolates(double temperature, double expected) => Assert.Equal(expected, CoolingEngine.Interpolate([new(30, 30), new(50, 50), new(80, 80), new(90, 100)], temperature));
    [Fact] public void DuplicateTemperaturesAreRejected() => Assert.Throws<InvalidDataException>(() => CoolingEngine.Interpolate([new(30, 30), new(30, 70)], 40));
    [Fact]
    public void StaleSensorImmediatelyForcesFullCooling()
    {
        var now = DateTimeOffset.UtcNow;
        var p = new FanPolicy { ChannelId = "f", Sources = [new() { SensorId = "cpu" }] };
        var engine = new CoolingEngine();
        _ = engine.Evaluate(p, [Temperature(30, now)], now);
        Assert.Equal(100, engine.Evaluate(p, [Temperature(30, now.AddSeconds(-10))], now.AddSeconds(1)));
    }
    [Fact]
    public void ManualModeRetainsTemperatureProtection()
    {
        var now = DateTimeOffset.UtcNow;
        var p = new FanPolicy { Manual = true, ManualDuty = 30, Sources = [new() { SensorId = "cpu" }] };
        Assert.Equal(100, new CoolingEngine().Evaluate(p, [Temperature(95, now)], now));
    }
    [Fact]
    public void CoolingRampLimitsDownwardChanges()
    {
        var now = DateTimeOffset.UtcNow;
        var p = new FanPolicy { ChannelId = "f", FallPerSecond = 5, Sources = [new() { SensorId = "cpu" }] };
        var engine = new CoolingEngine();
        Assert.Equal(100, engine.Evaluate(p, [Temperature(90, now)], now));
        Assert.Equal(95, engine.Evaluate(p, [Temperature(30, now.AddSeconds(1))], now.AddSeconds(1)));
    }
    [Fact]
    public void HysteresisPreventsSmallDownwardOscillation()
    {
        var now = DateTimeOffset.UtcNow;
        var p = new FanPolicy { ChannelId = "f", Hysteresis = 3, Sources = [new() { SensorId = "cpu" }] };
        var e = new CoolingEngine();
        var initial = e.Evaluate(p, [Temperature(60, now)], now);
        Assert.Equal(initial, e.Evaluate(p, [Temperature(59, now.AddSeconds(1))], now.AddSeconds(1)));
    }
    [Theory]
    [InlineData(MixMode.Maximum, 70)]
    [InlineData(MixMode.Average, 50)]
    public void MixCombinesCurveOutputs(MixMode mix, double expected)
    {
        var now = DateTimeOffset.UtcNow;
        var p = new FanPolicy { Mix = mix, Sources = [new() { SensorId = "cpu", Points = [new(0, 30), new(100, 30)] }, new() { SensorId = "gpu", Points = [new(0, 70), new(100, 70)] }] };
        Assert.Equal(expected, new CoolingEngine().Evaluate(p, [Temperature(40, now), new("gpu", "GPU", SensorKind.Temperature, 40, "°C", now, "gpu")], now));
    }
    [Fact]
    public void ImportedProfilesCannotActivateForeignFanMappings()
    {
        var p = new Profile { Fans = [new() { ChannelId = "fan", Enabled = true, Calibrated = true, AllowStop = true, Sources = [new() { SensorId = "cpu" }] }], Layout = [new() { DeviceId = "openrgb:123" }] };
        var imported = SettingsStore.Import(Json.Write(p));
        Assert.False(imported.Fans[0].Enabled);
        Assert.False(imported.Fans[0].Calibrated);
        Assert.Empty(imported.Fans[0].ChannelId);
        Assert.Empty(imported.Fans[0].Sources[0].SensorId);
        Assert.StartsWith("virtual:", imported.Layout[0].DeviceId);
    }
    [Fact] public void UnknownSettingsVersionsAreRejected() => Assert.Throws<InvalidDataException>(() => SettingsStore.Validate(new() { Version = 2 }));
    [Fact] public void NonFiniteValuesAreRejected() => Assert.Throws<InvalidDataException>(() => SettingsStore.ValidateProfile(new() { Layers = [new() { Speed = double.NaN }] }));
    [Fact]
    public void GraphRejectsCycles()
    {
        var e = new EffectLayer { Kind = EffectKind.Graph, OutputNode = "a", Nodes = [new() { Id = "a", Type = "Transform", Inputs = ["b"] }, new() { Id = "b", Type = "Transform", Inputs = ["a"] }] };
        Assert.Throws<InvalidDataException>(() => Effects.ValidateGraph(e));
    }
    [Fact]
    public void GraphRejectsMissingInputs()
    {
        var e = new EffectLayer { OutputNode = "a", Nodes = [new() { Id = "a", Type = "Blend", Inputs = ["missing", "missing"] }] };
        Assert.Throws<InvalidDataException>(() => Effects.ValidateGraph(e));
    }
    [Fact]
    public void GraphBlendProducesExpectedColor()
    {
        var e = new EffectLayer { Kind = EffectKind.Graph, OutputNode = "out", Nodes = [new() { Id = "a", Color = "#FF0000" }, new() { Id = "b", Color = "#0000FF" }, new() { Id = "out", Type = "Blend", Inputs = ["a", "b"], Value = .5 }] };
        Effects.ValidateGraph(e);
        Assert.Equal(new Rgb(127, 0, 127), Effects.Evaluate(e, .5, .5, Context()));
    }
    [Fact]
    public void TimelineInterpolatesAndWraps()
    {
        List<Keyframe> k = [new() { Time = 0, Color = "#FF0000" }, new() { Time = 2, Color = "#0000FF" }];
        Assert.Equal(new Rgb(127, 0, 127), Effects.Timeline(k, 1));
        Assert.Equal(new Rgb(255, 0, 0), Effects.Timeline(k, 2));
    }
    [Fact]
    public void TimelineHoldPreservesDiscreteFrames()
    {
        List<Keyframe> k = [new() { Time = 0, Color = "#FF0000", Hold = true }, new() { Time = 2, Color = "#0000FF" }];
        Assert.Equal(new Rgb(255, 0, 0), Effects.Timeline(k, 1));
    }
    [Theory]
    [InlineData(EffectKind.Static)]
    [InlineData(EffectKind.Breathing)]
    [InlineData(EffectKind.Pulse)]
    [InlineData(EffectKind.Rainbow)]
    [InlineData(EffectKind.Wave)]
    [InlineData(EffectKind.Chase)]
    [InlineData(EffectKind.Gradient)]
    [InlineData(EffectKind.Sparkle)]
    [InlineData(EffectKind.Ripple)]
    [InlineData(EffectKind.Temperature)]
    [InlineData(EffectKind.FanSpeed)]
    [InlineData(EffectKind.Audio)]
    [InlineData(EffectKind.Screen)]
    [InlineData(EffectKind.Game)]
    [InlineData(EffectKind.Timeline)]
    [InlineData(EffectKind.Graph)]
    public void EffectsRenderExactFrameLength(EffectKind kind)
    {
        var frame = Effects.Render(new()
        {
            Layers = [new() { Kind = kind, SensorId = "cpu" }]
        }, new()
        {
            LedCount = 37
        }, Context(1.5));
        Assert.Equal(37, frame.Length);
        Assert.All(frame, c => Assert.NotNull(c));
    }
    [Fact] public void MissingTemperatureProducesNoFabricatedValue() => Assert.Equal(Rgb.Black, Effects.Evaluate(new() { Kind = EffectKind.Temperature, SensorId = "missing" }, 0, 0, Context()));
    [Fact]
    public void AutomationUsesPriorityAndHonorsManualOverride()
    {
        var c = new AutomationContext(DateTimeOffset.Now, new(StringComparer.OrdinalIgnoreCase) { "game" }, [], 100, true, []);
        AutomationRule[] rules = [new() { Name = "a", Source = "game.exe", ProfileId = "low", Priority = 1 }, new() { Name = "b", Source = "game", ProfileId = "high", Priority = 20 }];
        Assert.Equal("high", Automations.Select(rules, c));
        Assert.Null(Automations.Select(rules, c, c.Now.AddMinutes(1)));
    }
    [Fact]
    public void OvernightScheduleWorks()
    {
        var c = new AutomationContext(new(2026, 10, 24, 1, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 24))), [], [], 0, true, []);
        Assert.True(Automations.Match(new()
        {
            Trigger = TriggerKind.Schedule,
            Start = "22:00",
            End = "06:00"
        }, c));
    }
    [Fact]
    public void UndoRedoRoundTrips()
    {
        var h = new UndoHistory<Profile>();
        var p = new Profile();
        h.Record(p);
        p.Name = "Changed";
        var undone = h.Undo(p);
        Assert.Equal("Studio", undone.Name);
        Assert.Equal("Changed", h.Redo(undone).Name);
    }
    [Fact]
    public async Task FramingRejectsOversizedMessages()
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(4_000_001));
        await Assert.ThrowsAsync<InvalidDataException>(() => Framing.Read(stream, CancellationToken.None));
    }
    [Fact]
    public async Task FramingRoundTripsUnicode()
    {
        using var stream = new MemoryStream();
        await Framing.Write(stream, "温度 · 40 °C", CancellationToken.None);
        stream.Position = 0;
        Assert.Equal("温度 · 40 °C", await Framing.Read(stream, CancellationToken.None));
    }
    [Fact] public void OpenRgbRejectsTruncatedDescriptions() => Assert.ThrowsAny<Exception>(() => OpenRgb.Parse([1, 2, 3, 4, 5]));
    [Fact]
    public void OpenRgbParsesProtocolThree()
    {
        using var m = new MemoryStream();
        using var w = new BinaryWriter(m);
        w.Write(0);
        w.Write(0);
        void S(string s)
        {
            var b = Encoding.UTF8.GetBytes(s + '\0');
            w.Write((ushort)b.Length);
            w.Write(b);
        }
        S("Fixture strip");
        S("Tests");
        S("Fixture");
        S("1");
        S("serial");
        S("usb:test");
        w.Write((ushort)0);
        w.Write(0);
        w.Write((ushort)1);
        S("Zone");
        w.Write(1);
        w.Write(0);
        w.Write(100);
        w.Write(2);
        w.Write((ushort)0);
        w.Write((ushort)2);
        S("LED1");
        w.Write(0);
        S("LED2");
        w.Write(1);
        w.Write((ushort)2);
        w.Write(0u);
        w.Write(0u);
        m.Position = 0;
        w.Write((int)m.Length);
        var d = OpenRgb.Parse(m.ToArray());
        Assert.Equal(2, d.LedCount);
        Assert.Equal("Fixture strip", d.Name);
        Assert.True(d.Zones[0].Resizable);
    }
    [Fact]
    public void TelemetryPersistsAndExportsWithoutNullReadings()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using (var db = new TelemetryStore(Path.Combine(root, "test.db")))
            {
                db.Append([Temperature(42, DateTimeOffset.UtcNow), Temperature(null, DateTimeOffset.UtcNow)]);
                Assert.Single(db.Read("cpu", DateTimeOffset.UtcNow.AddMinutes(-1)));
                db.Export(Path.Combine(root, "data.csv"));
                Assert.Contains("42", File.ReadAllText(Path.Combine(root, "data.csv")));
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}
