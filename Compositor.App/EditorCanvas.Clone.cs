using System.Windows.Input;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;

namespace Compositor.App;
public sealed partial class EditorCanvas
{
    private PointD? clonePointer;
    private sealed record ClonePreviewKey(Document Document, Guid? Layer, PointD Center, CloneSettings Settings, BrushSettings Brush, double Scale);
    private ClonePreviewKey? clonePreviewKey;
    private SKImage? clonePreview;
    private void ClearClonePreview() { clonePreview?.Dispose(); clonePreview = null; clonePreviewKey = null; }
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e); clonePointer = null;
        if (Tool == EditorTool.CloneStamp) InvalidateVisual();
    }
    internal PointD? CloneIndicatorSource => clonePointer is { } point
        ? CloneSource.SamplePoint(point, ReadClone().Aligned, stroke is not null || cloneStroke is not null) : CloneSource.Source;

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
            BrushSettings brush;
            try { brush = ReadBrush(); }
            catch (System.IO.InvalidDataException) { return; }
            float radius = (float)(brush.Diameter / 2);
            var settings = ReadClone();
            if (stroke is null && cloneStroke is null && CloneSource.OffsetAt(pointer, settings.Aligned) is { } offset)
            {
                var center = new PointD(pointer.X + offset.X, pointer.Y + offset.Y);
                var key = new ClonePreviewKey(Session.Document, Session.ActiveLayerId, center, settings, brush,
                    Zoom * System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX);
                if (key != clonePreviewKey)
                {
                    ClearClonePreview();
                    var raster = CloneStamp.Preview(key.Document, key.Layer, key.Center, key.Settings, key.Brush, key.Scale);
                    using var bitmap = new SKBitmap(CanvasRenderer.Info(raster.Width, raster.Height)); raster.ToRgba().CopyTo(bitmap.GetPixelSpan());
                    bitmap.SetImmutable(); clonePreview = SKImage.FromBitmap(bitmap); clonePreviewKey = key;
                }
                if (clonePreview is not null) canvas.DrawImage(clonePreview,
                    new SKRect((float)pointer.X - radius, (float)pointer.Y - radius, (float)pointer.X + radius, (float)pointer.Y + radius), new SKSamplingOptions(SKFilterMode.Linear));
            }
            canvas.DrawCircle((float)pointer.X, (float)pointer.Y, radius, under);
            canvas.DrawCircle((float)pointer.X, (float)pointer.Y, radius, line);
        }
    }
}
