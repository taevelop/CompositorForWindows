using System.Windows.Input;
using Compositor.Core;
using SkiaSharp;

namespace Compositor.App;
public sealed partial class EditorCanvas
{
    private PointD? clonePointer;
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e); clonePointer = null;
        if (Tool == EditorTool.CloneStamp) InvalidateVisual();
    }
    internal PointD? CloneIndicatorSource => clonePointer is { } point
        ? CloneSource.SamplePoint(point, ReadClone().Aligned, stroke is not null) : CloneSource.Source;

    private void DrawClone(SKCanvas canvas)
    {
        if (Tool != EditorTool.CloneStamp || Session.EditMask) return;
        using var under = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = (float)(3 / Zoom), IsAntialias = true };
        using var line = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = (float)(1 / Zoom), IsAntialias = true };
        if (CloneIndicatorSource is { } source)
        {
            float x = (float)source.X, y = (float)source.Y, size = (float)(7 / Zoom);
            foreach (var paint in new[] { under, line })
            {
                canvas.DrawLine(x - size, y, x + size, y, paint);
                canvas.DrawLine(x, y - size, x, y + size, paint);
                canvas.DrawCircle(x, y, (float)(3 / Zoom), paint);
            }
        }
        if (clonePointer is { } pointer && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            float radius = (float)(ReadBrush().Diameter / 2);
            canvas.DrawCircle((float)pointer.X, (float)pointer.Y, radius, under);
            canvas.DrawCircle((float)pointer.X, (float)pointer.Y, radius, line);
        }
    }
}
