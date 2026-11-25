using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using MkRgb.Core;

namespace MkRgb.Desktop;

/// <summary>Animated LED strip that renders a quick-lighting layer with the same engine the devices use.</summary>
public sealed class EffectPreview : FrameworkElement
{
    static readonly Dictionary<string, double> none = [];
    readonly Stopwatch clock = Stopwatch.StartNew();
    TimeSpan lastFrame;
    public EffectLayer Layer { get; set; } = new() { Kind = EffectKind.Static };
    public int Leds { get; set; } = 24;
    public bool ReducedMotion { get; set; }

    public EffectPreview()
    {
        Height = 72;
        System.Windows.Automation.AutomationProperties.SetName(this, "Effect preview");
        Loaded += (_, _) => CompositionTarget.Rendering += Tick;
        Unloaded += (_, _) => CompositionTarget.Rendering -= Tick;
    }

    void Tick(object? sender, EventArgs e)
    {
        // 30 fps is plenty for a preview; reduced motion drops to a still image refreshed twice a second.
        var interval = ReducedMotion ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromMilliseconds(33);
        if (clock.Elapsed - lastFrame < interval)
            return;
        lastFrame = clock.Elapsed;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(9, 11, 13)), new Pen(new SolidColorBrush(Color.FromRgb(43, 49, 55)), 1), new Rect(0.5, 0.5, width - 1, height - 1), 10, 10);
        var placement = new Placement { DeviceId = "preview", LedCount = Leds, Lighting = Layer };
        Rgb[] colors;
        try
        {
            colors = Effects.Render(new Profile(), placement, new EffectContext(ReducedMotion ? 0 : clock.Elapsed.TotalSeconds, none, [], Rgb.Black, none));
        }
        catch (FormatException) { return; } // A half-typed hex value; keep the last frame.
        var pitch = (width - 32) / Leds;
        var radius = Math.Min(pitch * 0.32, height * 0.16);
        for (var i = 0; i < colors.Length; i++)
        {
            var c = colors[i];
            var color = Color.FromRgb(c.R, c.G, c.B);
            var center = new Point(16 + pitch * (i + 0.5), height / 2);
            var glow = new RadialGradientBrush(Color.FromArgb(110, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B));
            dc.DrawEllipse(glow, null, center, radius * 2.6, radius * 2.6);
            dc.DrawEllipse(new SolidColorBrush(color), null, center, radius, radius);
        }
    }
}
