using System.Collections.Immutable;
using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

/// <summary>Mac-compatible PNG: 8-bit grayscale, no alpha, coverage is not color managed.</summary>
public static class MaskCodec
{
    public static LayerMask Load(string path, long remainingPixels)
    {
        if (new FileInfo(path).Length > ImageCodec.MaxEncodedBytes) throw new InvalidDataException("Mask asset is too large.");
        using var input = File.OpenRead(path);
        Span<byte> header = stackalloc byte[26]; input.ReadExactly(header); input.Position = 0;
        if (!header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !header.Slice(12, 4).SequenceEqual("IHDR"u8) || header[24] != 8 || header[25] != 0)
            throw new InvalidDataException("Mask PNG must be 8-bit grayscale without alpha.");
        using var codec = SKCodec.Create(input) ?? throw new InvalidDataException("Invalid mask PNG.");
        int width = codec.Info.Width, height = codec.Info.Height;
        Limits.CheckDimensions(width, height);
        if ((long)width * height > remainingPixels) throw new InvalidDataException("The project exceeds 100 megapixels of masks.");
        if (codec.Info.AlphaType != SKAlphaType.Opaque || codec.EncodedOrigin != SKEncodedOrigin.TopLeft)
            throw new InvalidDataException("Mask transparency and orientation metadata are not supported.");
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, new SKCodecOptions()) != SKCodecResult.Success)
            throw new InvalidDataException("Damaged mask PNG.");
        var source = bitmap.GetPixelSpan(); var tiles = ImmutableDictionary.CreateBuilder<TileKey, PixelTile>();
        for (int y = 0; y < height; y += 256)
        for (int x = 0; x < width; x += 256)
        {
            var bytes = new byte[PixelTile.ByteCount];
            for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
            for (int row = 0; row < Math.Min(256, height - y); row++)
            for (int col = 0; col < Math.Min(256, width - x); col++)
            {
                int p = (row * 256 + col) * 4; byte value = source[(y + row) * bitmap.RowBytes + x + col];
                bytes[p] = bytes[p + 1] = bytes[p + 2] = value;
            }
            tiles[new(x / 256, y / 256)] = new(bytes);
        }
        return new(new Raster(width, height, tiles.ToImmutable()));
    }

    public static void Save(LayerMask mask, string path)
    {
        var raster = mask.Pixels;
        using var bitmap = new SKBitmap(new SKImageInfo(raster.Width, raster.Height, SKColorType.Gray8, SKAlphaType.Opaque));
        var output = bitmap.GetPixelSpan(); output.Clear();
        foreach (var (key, tile) in raster.Tiles)
        for (int y = 0; y < Math.Min(256, raster.Height - key.Y * 256); y++)
        for (int x = 0; x < Math.Min(256, raster.Width - key.X * 256); x++)
            output[(key.Y * 256 + y) * bitmap.RowBytes + key.X * 256 + x] = tile.Bytes[(y * 256 + x) * 4];
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new IOException("Mask encoding failed.");
        using var file = File.Create(path); encoded.SaveTo(file); file.Flush(true);
    }
}
