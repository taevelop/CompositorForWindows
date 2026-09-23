using Compositor.Core;

namespace Compositor.Imaging;

/// <summary>Baked, alpha-preserving edits of immutable sRGB image tiles. No mask or composite processing.</summary>
public static class ColorAdjustments
{
    public static Raster Apply(Raster source, ColorAdjustment settings, CancellationToken cancellation = default)
    {
        settings.Validate(); cancellation.ThrowIfCancellationRequested();
        if (settings == default) return source;
        double saturation = 1 + settings.Saturation / 100;
        double contrast = settings.Contrast <= 0 ? 1 + settings.Contrast / 100 : 1 + settings.Contrast / 25;
        double brightness = settings.Brightness / 100;
        var tiles = source.Tiles.ToBuilder(); bool changed = false;
        // Reuse one scratch tile; only changed tiles are copied into immutable storage.
        var buffer = new byte[PixelTile.ByteCount];
        foreach (var (key, tile) in source.Tiles)
        {
            cancellation.ThrowIfCancellationRequested(); tile.Bytes.CopyTo(buffer);
            int width = Math.Min(256, source.Width - key.X * 256), height = Math.Min(256, source.Height - key.Y * 256);
            for (int y = 0; y < height; y++)
            {
                cancellation.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    int p = y * PixelTile.Stride + x * 4, alpha = buffer[p + 3];
                    if (alpha == 0) continue;
                    double r = buffer[p] / (double)alpha, g = buffer[p + 1] / (double)alpha, b = buffer[p + 2] / (double)alpha;
                    double gray = .2126 * r + .7152 * g + .0722 * b;
                    buffer[p] = Channel(r); buffer[p + 1] = Channel(g); buffer[p + 2] = Channel(b);
                    byte Channel(double value)
                    {
                        double adjusted = ((gray + (value - gray) * saturation) - .5) * contrast + .5 + brightness;
                        return (byte)Math.Round(Math.Clamp(adjusted, 0, 1) * alpha, MidpointRounding.AwayFromZero);
                    }
                }
            }
            if (!tile.Bytes.SequenceEqual(buffer)) { tiles[key] = new(buffer); changed = true; }
        }
        cancellation.ThrowIfCancellationRequested();
        return changed ? new(source.Width, source.Height, tiles.ToImmutable()) : source;
    }
}
