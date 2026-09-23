using System.Collections.Immutable;

namespace Compositor.Core;

/// <summary>Layer-local, linked grayscale coverage. RGB stores coverage; alpha is always opaque.</summary>
public sealed record LayerMask
{
    public Raster Pixels { get; }
    public bool Enabled { get; init; }

    public LayerMask(Raster pixels, bool enabled = true) : this(pixels, enabled, null) { }
    private LayerMask(Raster pixels, bool enabled, Raster? previous)
    {
        int expected = ((pixels.Width + 255) / 256) * ((pixels.Height + 255) / 256);
        if (pixels.Tiles.Count != expected) throw new InvalidDataException("Mask tiles must cover the entire image.");
        var known = previous?.Tiles.Values.ToHashSet() ?? [];
        foreach (var tile in pixels.Tiles.Values.Distinct())
        {
            if (known.Contains(tile)) continue;
            var bytes = tile.Bytes;
            // Include padding too, so all mask tiles have the same invariant.
            for (int i = 0; i < bytes.Length; i += 4)
                if (bytes[i] != bytes[i + 1] || bytes[i] != bytes[i + 2] || bytes[i + 3] != 255)
                    throw new InvalidDataException("Masks must be opaque grayscale images.");
        }
        Pixels = pixels; Enabled = enabled;
    }
    public LayerMask WithPixels(Raster pixels) => ReferenceEquals(Pixels, pixels) ? this : new(pixels, Enabled, Pixels);

    public static LayerMask Solid(int width, int height, byte coverage = 255)
    {
        Limits.CheckDimensions(width, height);
        var bytes = new byte[PixelTile.ByteCount];
        for (int i = 0; i < bytes.Length; i += 4) { bytes[i] = bytes[i + 1] = bytes[i + 2] = coverage; bytes[i + 3] = 255; }
        var tile = new PixelTile(bytes); var tiles = ImmutableDictionary.CreateBuilder<TileKey, PixelTile>();
        for (int y = 0; y < height; y += 256) for (int x = 0; x < width; x += 256) tiles[new(x / 256, y / 256)] = tile;
        return new(new Raster(width, height, tiles.ToImmutable()));
    }
    public Raster EditingPixels(int width, int height) => Pixels.Width == 1 && Pixels.Height == 1
        ? Solid(width, height, Pixels.Tiles[new(0, 0)].Bytes[0]).Pixels : Pixels;
}
