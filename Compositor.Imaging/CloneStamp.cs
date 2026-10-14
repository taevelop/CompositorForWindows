using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

public sealed record CloneSettings(bool Aligned = true, bool SampleAllLayers = false);

/// <summary>Document-local source alignment. A stroke captures its source before any pixels change.</summary>
public sealed class CloneSource
{
    public PointD? Source { get; private set; }
    private PointD? offset;

    public void Set(PointD point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;
        Source = point; offset = null;
    }

    public PointD? OffsetAt(PointD point, bool aligned)
    {
        if (Source is not { } source) return null;
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) throw new ArgumentException("Invalid clone pointer.");
        return aligned && offset is { } saved ? saved : new(
            Math.Round(source.X - point.X, MidpointRounding.AwayFromZero),
            Math.Round(source.Y - point.Y, MidpointRounding.AwayFromZero));
    }

    public PointD Begin(PointD point, bool aligned)
    {
        var result = OffsetAt(point, aligned) ?? throw new InvalidOperationException("Alt-click a clone source first.");
        offset = result; return result;
    }

    public PointD? SamplePoint(PointD pointer, bool aligned, bool strokeActive)
        => Source is null ? null : offset is { } saved && (aligned || strokeActive)
            ? new(pointer.X + saved.X, pointer.Y + saved.Y) : Source;
}

public static class CloneStamp
{
    internal readonly record struct Color(double Red, double Green, double Blue, double Alpha);
    // Premultiplied bilinear sampling, pixel centers at n+.5; transparent beyond the canvas.
    internal static Color Sample(Raster raster, PointD point)
    {
        double px = point.X - .5, py = point.Y - .5;
        if (px < -1 || py < -1 || px >= raster.Width || py >= raster.Height) return default;
        int left = (int)Math.Floor(px), top = (int)Math.Floor(py);
        double fx = px - left, fy = py - top, red = 0, green = 0, blue = 0, alpha = 0;
        for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++)
        {
            int sx = left + x, sy = top + y;
            if (sx < 0 || sy < 0 || sx >= raster.Width || sy >= raster.Height || !raster.Tiles.TryGetValue(new(sx / 256, sy / 256), out var tile)) continue;
            double weight = (x == 0 ? 1 - fx : fx) * (y == 0 ? 1 - fy : fy);
            int p = ((sy % 256) * 256 + sx % 256) * 4; var bytes = tile.Bytes;
            red += bytes[p] * weight; green += bytes[p + 1] * weight; blue += bytes[p + 2] * weight; alpha += bytes[p + 3] * weight;
        }
        return new(red, green, blue, alpha);
    }

    /// <summary>Raw active pixels ignore masks/effects/opacity; all-layers samples the visible composite.</summary>
    public static Raster Capture(Document document, Guid? activeLayerId, CloneSettings settings, CancellationToken cancellation = default)
    {
        document.Validate(); cancellation.ThrowIfCancellationRequested();
        var sample = document;
        if (!settings.SampleAllLayers)
        {
            var layer = document.Layers.FirstOrDefault(l => l.Id == activeLayerId);
            sample = document with { Layers = layer is { IsGroup: false, IsAdjustment: false }
                ? [new Layer(layer.Id, layer.Name, layer.Pixels, layer.Transform)] : [] };
        }
        using var bitmap = new SKBitmap(CanvasRenderer.Info(document.Width, document.Height));
        using var renderer = new CanvasRenderer();
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent); renderer.DrawCancellable(canvas, sample, cancellation); canvas.Flush();
        }
        cancellation.ThrowIfCancellationRequested();
        return Raster.FromRgba(document.Width, document.Height, bitmap.GetPixelSpan());
    }
}
