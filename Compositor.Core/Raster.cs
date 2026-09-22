using System.Collections.Immutable;

namespace Compositor.Core;

/// <summary>Immutable, premultiplied RGBA8 in sRGB, with top-down rows.</summary>
public sealed class PixelTile
{
    public const int Side = 256;
    public const int Stride = Side * 4;
    public const int ByteCount = Stride * Side;
    private readonly byte[] bytes;
    public ReadOnlySpan<byte> Bytes => bytes;
    public PixelTile(ReadOnlySpan<byte> source)
    {
        if (source.Length != ByteCount) throw new ArgumentException("Invalid tile length.");
        bytes = source.ToArray();
    }
}

public readonly record struct TileKey(int X, int Y);

public sealed class Raster
{
    public int Width { get; }
    public int Height { get; }
    public ImmutableDictionary<TileKey, PixelTile> Tiles { get; }
    public long AllocatedBytes => (long)Tiles.Count * PixelTile.ByteCount;
    public Raster(int width, int height, ImmutableDictionary<TileKey, PixelTile>? tiles = null)
    {
        Limits.CheckDimensions(width, height);
        Width = width; Height = height;
        Tiles = tiles ?? ImmutableDictionary<TileKey, PixelTile>.Empty;
        if (Tiles.Keys.Any(k => k.X < 0 || k.Y < 0 || k.X * 256 >= width || k.Y * 256 >= height))
            throw new ArgumentException("Tile is outside the image.");
    }
    public static Raster FromRgba(int width, int height, ReadOnlySpan<byte> pixels)
    {
        Limits.CheckDimensions(width, height);
        if (pixels.Length != checked(width * height * 4)) throw new ArgumentException("Invalid pixel length.");
        var tiles = ImmutableDictionary.CreateBuilder<TileKey, PixelTile>();
        for (int y = 0; y < height; y += 256)
        for (int x = 0; x < width; x += 256)
        {
            var data = new byte[PixelTile.ByteCount];
            for (int row = 0; row < Math.Min(256, height - y); row++)
                pixels.Slice(((y + row) * width + x) * 4, Math.Min(256, width - x) * 4)
                    .CopyTo(data.AsSpan(row * PixelTile.Stride));
            if (data.AsSpan().IndexOfAnyExcept((byte)0) >= 0) tiles[new(x / 256, y / 256)] = new(data);
        }
        return new(width, height, tiles.ToImmutable());
    }
    public byte[] ToRgba()
    {
        var result = new byte[checked(Width * Height * 4)];
        foreach (var (key, tile) in Tiles)
        for (int row = 0; row < Math.Min(256, Height - key.Y * 256); row++)
            tile.Bytes.Slice(row * PixelTile.Stride, Math.Min(256, Width - key.X * 256) * 4)
                .CopyTo(result.AsSpan(((key.Y * 256 + row) * Width + key.X * 256) * 4));
        return result;
    }
}
public static class Limits
{
    public const long MaxPixels = 100_000_000;
    public static void CheckDimensions(int width, int height)
    {
        if (width is < 1 or > 30_000 || height is < 1 or > 30_000 || (long)width * height > MaxPixels)
            throw new InvalidDataException("Images must be 1–30,000 pixels per side and at most 100 megapixels.");
    }
}
