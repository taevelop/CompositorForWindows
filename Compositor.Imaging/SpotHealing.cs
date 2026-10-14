using Compositor.Core;

namespace Compositor.Imaging;
public enum HealingMode { ContentAware, CreateTexture, ProximityMatch }
public static class SpotHealing
{
    public static Raster Apply(Raster source, ReadOnlySpan<byte> coverage, double opacity = 1, HealingMode mode = HealingMode.ContentAware, uint seed = 0, CancellationToken cancellation = default)
    {
        if (coverage.Length != checked(source.Width * source.Height) || !double.IsFinite(opacity) || opacity is < 0 or > 1 || !Enum.IsDefined(mode))
            throw new ArgumentException("Invalid healing coverage or settings.");
        cancellation.ThrowIfCancellationRequested();if (opacity == 0) return source;
        int left = source.Width, top = source.Height, right = 0, bottom = 0;
        for (int y = 0; y < source.Height; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int x = 0; x < source.Width; x++) if (coverage[y * source.Width + x] != 0)
            { left = Math.Min(left, x);top = Math.Min(top, y);right = Math.Max(right, x + 1);bottom = y + 1; }
        }
        if (right <= left) return source;
        double reach = (Math.Max(right - left, bottom - top) + 32) * 3.2;
        left = (int)Math.Max(0, Math.Floor(left - reach));top = (int)Math.Max(0, Math.Floor(top - reach));
        right = (int)Math.Min(source.Width, Math.Ceiling(right + reach));bottom = (int)Math.Min(source.Height, Math.Ceiling(bottom + reach));
        int width = right - left, height = bottom - top;var pixels = new byte[checked(width * height * 4)];var mask = new byte[checked(width * height)];
        for (int y = top; y < bottom; y++) { cancellation.ThrowIfCancellationRequested();coverage.Slice(y * source.Width + left, width).CopyTo(mask.AsSpan((y - top) * width, width)); }
        foreach (var (key, tile) in source.Tiles)
        {
            cancellation.ThrowIfCancellationRequested();int x0 = Math.Max(left, key.X * 256), y0 = Math.Max(top, key.Y * 256);
            int x1 = Math.Min(right, key.X * 256 + 256), y1 = Math.Min(bottom, key.Y * 256 + 256);if (x1 <= x0) continue;
            for (int y = y0; y < y1; y++) tile.Bytes.Slice(((y - key.Y * 256) * 256 + x0 - key.X * 256) * 4, (x1 - x0) * 4)
                .CopyTo(pixels.AsSpan(((y - top) * width + x0 - left) * 4, (x1 - x0) * 4));
        }
        cancellation.ThrowIfCancellationRequested();int status = NativePixels.SpotHeal(pixels, mask, width, height, (float)opacity, (int)mode, seed);
        cancellation.ThrowIfCancellationRequested();if (status != 0) throw new OutOfMemoryException("Spot healing could not allocate its working buffers.");
        var tiles = source.Tiles.ToBuilder();bool changed = false;
        for (int ty = top / 256; ty <= (bottom - 1) / 256; ty++) for (int tx = left / 256; tx <= (right - 1) / 256; tx++)
        {
            cancellation.ThrowIfCancellationRequested();var key = new TileKey(tx, ty);source.Tiles.TryGetValue(key, out var original);
            var output = original is null ? new byte[PixelTile.ByteCount] : original.Bytes.ToArray();
            int x0 = Math.Max(left, tx * 256), x1 = Math.Min(right, tx * 256 + 256), y0 = Math.Max(top, ty * 256), y1 = Math.Min(bottom, ty * 256 + 256);
            for (int y = y0; y < y1; y++) pixels.AsSpan(((y - top) * width + x0 - left) * 4, (x1 - x0) * 4).CopyTo(output.AsSpan(((y - ty * 256) * 256 + x0 - tx * 256) * 4));
            if (original is not null && original.Bytes.SequenceEqual(output)) continue;
            bool empty = output.AsSpan().IndexOfAnyExcept((byte)0) < 0;if (original is null && empty) continue;
            changed = true;if (empty) tiles.Remove(key);else tiles[key] = new(output);
        }
        cancellation.ThrowIfCancellationRequested();return changed ? new Raster(source.Width, source.Height, tiles.ToImmutable()) : source;
    }
}
