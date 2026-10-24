using System.Windows;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using MkRgb.Core;

namespace MkRgb.Desktop;

public sealed class NodeCanvas : SKElement
{
    readonly EffectLayer layer; EffectNode? dragging; Point anchor; EffectNode? connecting;
    public NodeCanvas(EffectLayer effect, MainViewModel vm)
    {
        layer = effect;
        Height = 300;
        for (var i = 0; i < layer.Nodes.Count; i++)
        {
            var n = layer.Nodes[i];
            if (n.X < 0)
                n.X = 30 + i % 4 * 220;
            if (n.Y < 0)
                n.Y = 35 + i / 4 * 100;
        }
        PaintSurface += Paint;
        vm.Frame += InvalidateVisual;
        Unloaded += (_, _) => vm.Frame -= InvalidateVisual;
        MouseLeftButtonDown += (_, e) =>
        {
            var p = e.GetPosition(this);
            var hit = layer.Nodes.LastOrDefault(n => p.X >= n.X && p.X <= n.X + 185 && p.Y >= n.Y && p.Y <= n.Y + 70);
            if (hit is null)
                return;
            vm.Record();
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                if (connecting is null)
                    connecting = hit;
                else
                {
                    var required = hit.Type == "Blend" ? 3 : hit.Type == "Mask" ? 2 : hit.Type is "Transform" or "Colorize" ? 1 : 0;
                    if (required > 0 && hit.Id != connecting.Id)
                    {
                        if (hit.Inputs.Count >= required)
                            hit.Inputs.RemoveAt(0);
                        hit.Inputs.Add(connecting.Id);
                    }
                    connecting = null;
                }
                InvalidateVisual();
                return;
            }
            dragging = hit;
            anchor = new(p.X - hit.X, p.Y - hit.Y);
            CaptureMouse();
        };
        MouseMove += (_, e) => { if (dragging is null) return; var p = e.GetPosition(this); dragging.X = Math.Max(0, p.X - anchor.X); dragging.Y = Math.Max(0, p.Y - anchor.Y); InvalidateVisual(); };
        MouseLeftButtonUp += (_, _) => { dragging = null; ReleaseMouseCapture(); };
    }
    void Paint(object? sender, SKPaintSurfaceEventArgs e)
    {
        var c = e.Surface.Canvas;
        c.Clear(SKColor.Parse("#131719"));
        c.Scale(e.Info.Width / (float)ActualWidth);
        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), 12);
        using var small = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), 10);
        using var line = new SKPaint { Color = SKColor.Parse("#78E8C2"), IsAntialias = true, StrokeWidth = 2, Style = SKPaintStyle.Stroke };
        foreach (var n in layer.Nodes)
            foreach (var input in n.Inputs)
            {
                var source = layer.Nodes.FirstOrDefault(x => x.Id == input);
                if (source != null)
                    c.DrawLine((float)source.X + 185, (float)source.Y + 35, (float)n.X, (float)n.Y + 35, line);
            }
        foreach (var n in layer.Nodes)
        {
            if (n.X < 0)
            {
                n.X = 30;
                n.Y = 30;
            }
            using var fill = new SKPaint { Color = SKColor.Parse(n == connecting ? "#36584B" : "#22292D"), IsAntialias = true };
            using var white = new SKPaint { Color = SKColor.Parse("#EDF0F1"), IsAntialias = true };
            using var muted = new SKPaint { Color = SKColor.Parse("#95A4AD"), IsAntialias = true };
            c.DrawRoundRect((float)n.X, (float)n.Y, 185, 70, 6, 6, fill);
            c.DrawText(n.Type, (float)n.X + 14, (float)n.Y + 25, SKTextAlign.Left, font, white);
            c.DrawText(n.Id + (n.Id == layer.OutputNode ? "  • OUTPUT" : ""), (float)n.X + 14, (float)n.Y + 49, SKTextAlign.Left, small, muted);
            c.DrawCircle((float)n.X, (float)n.Y + 35, 4, line);
            c.DrawCircle((float)n.X + 185, (float)n.Y + 35, 4, line);
        }
    }
}
