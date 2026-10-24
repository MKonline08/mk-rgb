using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MkRgb.Core;

namespace MkRgb.Desktop;

/// <summary>Frame-snapped timeline with keyboard editing and a sampled color preview.</summary>
public sealed class TimelineCanvas : FrameworkElement
{
    readonly EffectLayer layer;
    readonly MainViewModel vm;
    Keyframe? selected;
    public int FramesPerSecond { get; set; } = 30;
    double Duration => Math.Max(5, layer.Keyframes.Select(k => k.Time).DefaultIfEmpty(5).Max() + .5);
    double WidthInPixels => Math.Max(1, ActualWidth - 48);
    public TimelineCanvas(EffectLayer layer, MainViewModel vm)
    {
        this.layer = layer;
        this.vm = vm;
        Height = 125;
        Focusable = true;
        System.Windows.Automation.AutomationProperties.SetName(this, "Keyframe timeline. Tab to focus, left or right to move a selected keyframe by one frame.");
        vm.Frame += InvalidateVisual;
        Unloaded += (_, _) => vm.Frame -= InvalidateVisual;
        MouseLeftButtonDown += (_, e) =>
        {
            Focus();
            var x = e.GetPosition(this).X;
            selected = layer.Keyframes.OrderBy(k => Math.Abs(24 + k.Time / Duration * WidthInPixels - x)).FirstOrDefault();
            if (selected != null)
            {
                vm.Record();
                CaptureMouse();
                MoveTo(x);
            }
        };
        MouseMove += (_, e) => { if (IsMouseCaptured) MoveTo(e.GetPosition(this).X); };
        MouseLeftButtonUp += (_, _) => ReleaseMouseCapture();
        KeyDown += (_, e) =>
        {
            selected ??= layer.Keyframes.FirstOrDefault();
            if (selected == null || e.Key is not (Key.Left or Key.Right))
                return;
            vm.Record();
            SetTime(selected.Time + (e.Key == Key.Left ? -1d : 1d) / FramesPerSecond);
            e.Handled = true;
        };
    }
    void MoveTo(double x) => SetTime(Math.Round(Math.Clamp((x - 24) / WidthInPixels, 0, 1) * Duration * FramesPerSecond) / FramesPerSecond);
    void SetTime(double time)
    {
        time = Math.Max(0, time);
        if (selected != null && !layer.Keyframes.Any(k => k != selected && Math.Abs(k.Time - time) < .00001))
            selected.Time = time;
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(19, 23, 25)), null, new Rect(0, 0, ActualWidth, ActualHeight), 5, 5);
        var ordered = layer.Keyframes.OrderBy(k => k.Time).ToList();
        for (var i = 0; i < 100; i++)
        {
            var time = i / 100d * Duration;
            var a = ordered.LastOrDefault(k => k.Time <= time) ?? ordered.FirstOrDefault();
            var b = ordered.FirstOrDefault(k => k.Time > time) ?? a;
            if (a == null || b == null)
                continue;
            try
            {
                var c = a.Hold || a == b ? Rgb.Parse(a.Color) : Rgb.Lerp(Rgb.Parse(a.Color), Rgb.Parse(b.Color), (time - a.Time) / (b.Time - a.Time));
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)), null, new Rect(24 + i / 100d * WidthInPixels, 28, WidthInPixels / 100 + 1, 35));
            }
            catch (FormatException) { }
        }
        foreach (var k in ordered)
        {
            var x = 24 + k.Time / Duration * WidthInPixels;
            dc.DrawEllipse(k == selected ? Brushes.White : new SolidColorBrush(Color.FromRgb(120, 232, 194)), new Pen(Brushes.Black, 2), new Point(x, 76), 6, 6);
            var label = new FormattedText($"{k.Time:0.##}s", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.LightGray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(label, new Point(x - 8, 92));
        }
    }
}
