using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

/// <summary>Applies an adjustment to source image pixels, independently of layer placement, mask and effects.</summary>
public static class PixelAdjustments
{
    public static Raster Apply(Raster source, Layer adjustment, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!adjustment.IsAdjustment || adjustment.IsGroup) throw new ArgumentException("Expected an adjustment.", nameof(adjustment));
        var normalized = adjustment with
        {
            Pixels = new(source.Width, source.Height), Transform = new(0, 0, source.Width, source.Height),
            Opacity = 1, Visible = true, Blend = BlendMode.Normal, Mask = null, ParentId = null, Effects = null
        };
        var document = Document.Create(source.Width, source.Height) with { Layers = [normalized] };
        document.Validate();
        if (source.Tiles.Count == 0 || AdjustmentProcessor.IsIdentity(normalized)) return source;
        using var bitmap = new SKBitmap(CanvasRenderer.Info(source.Width, source.Height));
        source.ToRgba().CopyTo(bitmap.GetPixelSpan());
        AdjustmentProcessor.Apply(bitmap, document, normalized, 1);
        cancellation.ThrowIfCancellationRequested();
        var pixels = bitmap.GetPixelSpan(); var tiles = source.Tiles.ToBuilder(); bool changed = false;
        var buffer = new byte[PixelTile.ByteCount];
        // All supported adjustments preserve alpha, so no formerly empty tile can gain pixels.
        foreach (var (key, tile) in source.Tiles)
        {
            cancellation.ThrowIfCancellationRequested(); Array.Clear(buffer);
            int width = Math.Min(256, source.Width - key.X * 256), height = Math.Min(256, source.Height - key.Y * 256);
            for (int row = 0; row < height; row++)
                pixels.Slice(((key.Y * 256 + row) * source.Width + key.X * 256) * 4, width * 4).CopyTo(buffer.AsSpan(row * PixelTile.Stride));
            if (tile.Bytes.SequenceEqual(buffer)) continue;
            changed = true; tiles[key] = new(buffer);
        }
        cancellation.ThrowIfCancellationRequested();
        return changed ? new(source.Width, source.Height, tiles.ToImmutable()) : source;
    }
}
