using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MkRgb.Core;

namespace MkRgb.Desktop;

/// <summary>Hue/saturation selection; brightness is controlled separately.</summary>
public sealed class ColorWheel : FrameworkElement
{
    static readonly BitmapSource wheel = CreateWheel();
    double hue, saturation = 1;
    public event Action<string>? ColorChanged;
    public string Hex { get; private set; } = "#FF0000";
    public ColorWheel()
    {
        Width = 232; Height = 232; Focusable = true; Cursor = Cursors.Cross;
        System.Windows.Automation.AutomationProperties.SetName(this, "Color wheel. Left and right change hue; up and down change saturation.");
        MouseLeftButtonDown += (_, e) => { Focus(); CaptureMouse(); Pick(e.GetPosition(this)); };
        MouseMove += (_, e) => { if (IsMouseCaptured) Pick(e.GetPosition(this)); };
        MouseLeftButtonUp += (_, _) => ReleaseMouseCapture();
        KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Left: hue -= 1d / 120; break;
                case Key.Right: hue += 1d / 120; break;
                case Key.Up: saturation = Math.Min(1, saturation + .03); break;
                case Key.Down: saturation = Math.Max(0, saturation - .03); break;
                default: return;
            }
            Update(); e.Handled = true;
        };
    }
    public static Rgb At(double x, double y) => Rgb.Hsv(Math.Atan2(y, x) / (Math.PI * 2), Math.Min(1, Math.Sqrt(x * x + y * y)));
    public void Select(string hex)
    {
        var rgb = Rgb.Parse(hex);
        var r = rgb.R / 255d; var g = rgb.G / 255d; var b = rgb.B / 255d;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min;
        hue = delta == 0 ? 0 : (max == r ? (g - b) / delta : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4) / 6;
        saturation = max == 0 ? 0 : delta / max;
        Hex = rgb.Hex;
        InvalidateVisual();
    }
    void Pick(Point point)
    {
        var radius = (Math.Min(ActualWidth, ActualHeight) - 16) / 2;
        var x = (point.X - ActualWidth / 2) / radius;
        var y = (point.Y - ActualHeight / 2) / radius;
        hue = Math.Atan2(y, x) / (Math.PI * 2);
        saturation = Math.Min(1, Math.Sqrt(x * x + y * y));
        Update();
    }
    void Update()
    {
        Hex = Rgb.Hsv(hue, saturation).Hex;
        InvalidateVisual();
        ColorChanged?.Invoke(Hex);
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var size = Math.Min(ActualWidth, ActualHeight) - 16;
        drawing.DrawImage(wheel, new((ActualWidth - size) / 2, (ActualHeight - size) / 2, size, size));
        var point = new Point(ActualWidth / 2 + Math.Cos(hue * Math.PI * 2) * saturation * size / 2,
            ActualHeight / 2 + Math.Sin(hue * Math.PI * 2) * saturation * size / 2);
        drawing.DrawEllipse(null, new Pen(Brushes.Black, 5), point, 7, 7);
        drawing.DrawEllipse(null, new Pen(Brushes.White, 2), point, 7, 7);
        if (IsKeyboardFocused) drawing.DrawRoundedRectangle(null, new Pen(Brushes.White, 1), new Rect(1, 1, ActualWidth - 2, ActualHeight - 2), 8, 8);
    }
    static BitmapSource CreateWheel()
    {
        const int size = 256;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var nx = (x - 127.5) / 127.5; var ny = (y - 127.5) / 127.5;
                if (nx * nx + ny * ny > 1) continue;
                var rgb = At(nx, ny); var i = (y * size + x) * 4;
                pixels[i] = rgb.B; pixels[i + 1] = rgb.G; pixels[i + 2] = rgb.R; pixels[i + 3] = 255;
            }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
