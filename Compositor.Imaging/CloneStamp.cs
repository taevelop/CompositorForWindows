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
