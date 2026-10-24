using System.Windows;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using MkRgb.Core;

namespace MkRgb.Desktop;

public sealed class SetupCanvas : SKElement
{
    readonly MainViewModel vm; readonly bool editable; Placement? dragging; Point anchor; double zoom = 1;
    public event Action<Placement>? Selected;
    public SetupCanvas(MainViewModel model, bool edit = false)
    {
        vm = model;
        editable = edit;
        Height = 350;
        Focusable = true;
        PaintSurface += Paint;
        MouseLeftButtonDown += Down;
        MouseMove += Move;
        MouseLeftButtonUp += (_, _) => { dragging = null; ReleaseMouseCapture(); };
        MouseWheel += (_, e) => { zoom = Math.Clamp(zoom + (e.Delta > 0 ? .1 : -.1), .5, 2.5); InvalidateVisual(); };
        vm.Frame += InvalidateVisual;
        Unloaded += (_, _) => vm.Frame -= InvalidateVisual;
    }
    static SKPaint PaintOf(string color, SKPaintStyle style = SKPaintStyle.Fill, float width = 1) => new() { Color = SKColor.Parse(color), IsAntialias = true, Style = style, StrokeWidth = width };
    void Paint(object? sender, SKPaintSurfaceEventArgs e)
    {
        var c = e.Surface.Canvas;
        c.Clear(SKColor.Parse("#131719"));
        var scale = e.Info.Width / 1000f * (float)zoom;
        c.Scale(scale);
        using var dot = PaintOf("#2B3236");
        for (var x = 20; x < 1000 / zoom; x += 25)
            for (var y = 20; y < e.Info.Height / scale; y += 25)
                c.DrawCircle(x, y, 1, dot);
        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), 12 / scale);
        using var muted = PaintOf("#8E9DA5");
        if (vm.Profile.Layout.Count == 0)
        {
            using var title = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), 24);
            using var white = PaintOf("#DCE4E3");
            c.DrawText("A place for every light.", 60, 95, SKTextAlign.Left, title, white);
            c.DrawText("Connect your RGB hardware or add a preview device to start composing.", 60, 128, SKTextAlign.Left, font, muted);
            using var line = PaintOf("#34413D", SKPaintStyle.Stroke);
            c.DrawRoundRect(60, 175, 240, 64, 10, 10, line);
            c.DrawCircle(350, 207, 32, line);
            c.DrawRoundRect(420, 175, 64, 64, 10, 10, line);
            return;
        }
        var ctx = vm.Context();
        if (vm.Settings.ReducedMotion)
            ctx = ctx with
            {
                Time = 0
            };
        foreach (var p in vm.Profile.Layout)
        {
            c.Save();
            c.Translate((float)p.X, (float)p.Y);
            c.RotateDegrees((float)p.Rotation);
            using var back = PaintOf("#20272B");
            using var border = PaintOf(p == dragging ? "#78E8C2" : "#40504C", SKPaintStyle.Stroke);
            c.DrawRoundRect(-9, -9, (float)p.Width + 18, (float)p.Height + 18, 8, 8, back);
            c.DrawRoundRect(-9, -9, (float)p.Width + 18, (float)p.Height + 18, 8, 8, border);
            Rgb[] colors;
            try
            {
                colors = Effects.Render(vm.Profile, p, ctx);
            }
            catch { colors = Enumerable.Repeat(Rgb.Black, p.LedCount).ToArray(); }
            var shown = Math.Min(colors.Length, 80);
            var step = (float)p.Width / shown;
            for (var i = 0; i < shown; i++)
            {
                var rgb = colors[i * colors.Length / shown];
                using var led = new SKPaint { Color = new(rgb.R, rgb.G, rgb.B), IsAntialias = true };
                c.DrawRoundRect(i * step, 0, Math.Max(2, step - 3), (float)p.Height, 3, 3, led);
            }
            c.DrawText(p.Name, 0, (float)p.Height + 30, SKTextAlign.Left, font, muted);
            c.Restore();
        }
        c.DrawText(editable ? "DRAG TO POSITION  ·  SCROLL TO ZOOM  ·  10 PX SNAP" : "LIGHTING PREVIEW", 24, 22 / scale, SKTextAlign.Left, font, muted);
    }
    void Down(object sender, MouseButtonEventArgs e)
    {
        if (!editable)
            return;
        var point = e.GetPosition(this);
        var x = point.X / ActualWidth * 1000 / zoom;
        var y = point.Y / ActualWidth * 1000 / zoom;
        dragging = vm.Profile.Layout.LastOrDefault(p => x >= p.X - 10 && x <= p.X + p.Width + 10 && y >= p.Y - 10 && y <= p.Y + p.Height + 35);
        if (dragging is null)
            return;
        vm.Record();
        anchor = new(x - dragging.X, y - dragging.Y);
        CaptureMouse();
        Selected?.Invoke(dragging);
    }
    void Move(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (dragging is null)
            return;
        var p = e.GetPosition(this);
        dragging.X = Math.Round((p.X / ActualWidth * 1000 / zoom - anchor.X) / 10) * 10;
        dragging.Y = Math.Round((p.Y / ActualWidth * 1000 / zoom - anchor.Y) / 10) * 10;
        InvalidateVisual();
    }
}
public sealed class PlotCanvas : SKElement
{
    public Func<List<(double X, double Y)>> Points { get; set; } = () => []; public double MaxX { get; set; } = 100; public double MaxY { get; set; } = 100;
    public string XLabel { get; set; } = "Temperature · °C"; public string YLabel { get; set; } = "Fan duty · %";
    public Action<int, double, double>? Edit
    {
        get; set;
    }
    int drag = -1;
    public Action? BeginEdit { get; set; }
    public PlotCanvas()
    {
        Height = 230;
        PaintSurface += Paint;
        MouseLeftButtonDown += (_, e) => { if (Edit is null) return; var p = e.GetPosition(this); var pts = Points(); drag = Enumerable.Range(0, pts.Count).OrderBy(i => Math.Abs(pts[i].X / MaxX * (ActualWidth - 65) + 45 - p.X)).FirstOrDefault(-1); if (drag >= 0) BeginEdit?.Invoke(); CaptureMouse(); };
        MouseMove += (_, e) => { if (drag < 0 || Edit is null) return; var p = e.GetPosition(this); Edit(drag, Math.Round(Math.Clamp((p.X - 45) / (ActualWidth - 65) * MaxX, 0, MaxX)), Math.Round(Math.Clamp((ActualHeight - 35 - p.Y) / (ActualHeight - 65) * MaxY, 0, MaxY))); InvalidateVisual(); };
        MouseLeftButtonUp += (_, _) => { drag = -1; ReleaseMouseCapture(); };
    }
    void Paint(object? sender, SKPaintSurfaceEventArgs e)
    {
        var c = e.Surface.Canvas;
        c.Clear(SKColor.Parse("#171C1F"));
        var scale = e.Info.Width / (float)ActualWidth;
        c.Scale(scale);
        var width = (float)ActualWidth;
        var height = (float)ActualHeight;
        using var grid = new SKPaint { Color = SKColor.Parse("#2B3338"), StrokeWidth = 1 };
        using var text = new SKPaint { Color = SKColor.Parse("#96A5AD"), IsAntialias = true };
        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), 10);
        for (var i = 0; i <= 4; i++)
        {
            var y = 30 + (height - 65) * i / 4;
            c.DrawLine(45, y, width - 20, y, grid);
            c.DrawText((MaxY * (4 - i) / 4).ToString("0"), 5, y + 4, SKTextAlign.Left, font, text);
        }
        c.DrawText(YLabel, 45, 16, SKTextAlign.Left, font, text);
        c.DrawText(XLabel, 45, height - 6, SKTextAlign.Left, font, text);
        var points = Points();
        if (points.Count == 0)
        {
            c.DrawText("Waiting for sensor history", 70, height / 2, SKTextAlign.Left, font, text);
            return;
        }
        using var path = new SKPathBuilder();
        for (var i = 0; i < points.Count; i++)
        {
            var x = 45 + (float)(points[i].X / MaxX) * (width - 65);
            var y = height - 35 - (float)(points[i].Y / MaxY) * (height - 65);
            if (i == 0)
                path.MoveTo(x, y);
            else
                path.LineTo(x, y);
        }
        using var stroke = new SKPaint { Color = SKColor.Parse("#78E8C2"), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        using var built = path.Detach();
        c.DrawPath(built, stroke);
        if (Edit != null)
            foreach (var p in points)
            {
                using var point = new SKPaint { Color = SKColor.Parse("#78E8C2"), IsAntialias = true };
                c.DrawCircle(45 + (float)(p.X / MaxX) * (width - 65), height - 35 - (float)(p.Y / MaxY) * (height - 65), 5, point);
            }
    }
}
