using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

public static class ImageCodec
{
    public const long MaxEncodedBytes = 512L * 1024 * 1024;
    public static Raster Load(string path, long remainingPixels = Limits.MaxPixels, bool pngOnly = false)
    {
        if (new FileInfo(path).Length > MaxEncodedBytes) throw new InvalidDataException("Image exceeds 512 MiB.");
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("The image cannot be decoded.");
        if (codec.EncodedFormat != SKEncodedImageFormat.Png && (pngOnly || codec.EncodedFormat != SKEncodedImageFormat.Jpeg))
            throw new NotSupportedException("This Windows build imports PNG and JPEG only.");
        int width = codec.Info.Width, height = codec.Info.Height;
        Limits.CheckDimensions(width, height);
        if ((long)width * height > remainingPixels) throw new InvalidDataException("The project exceeds 100 megapixels of source images.");
        using var bitmap = new SKBitmap(CanvasRenderer.Info(width, height));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success) throw new InvalidDataException("Incomplete or damaged image.");
        var bytes = new byte[width * height * 4]; Marshal.Copy(bitmap.GetPixels(), bytes, 0, bytes.Length);
        NativePixels.Clamp(bytes, checked(width * height));
        var origin = codec.EncodedOrigin;
        if (origin == SKEncodedOrigin.TopLeft) return Raster.FromRgba(width, height, bytes);
        bool swap = (int)origin >= 5; int outWidth = swap ? height : width, outHeight = swap ? width : height;
        var oriented = new byte[bytes.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            (int ox, int oy) = (int)origin switch
            {
                2 => (width - 1 - x, y), 3 => (width - 1 - x, height - 1 - y), 4 => (x, height - 1 - y),
                5 => (y, x), 6 => (height - 1 - y, x), 7 => (height - 1 - y, width - 1 - x),
                8 => (y, width - 1 - x), _ => (x, y)
            };
            bytes.AsSpan((y * width + x) * 4, 4).CopyTo(oriented.AsSpan((oy * outWidth + ox) * 4));
        }
        return Raster.FromRgba(outWidth, outHeight, oriented);
    }

    public static unsafe void SaveRaster(Raster raster, string path)
    {
        var bytes = raster.ToRgba();
        fixed (byte* p = bytes)
        {
            using var image = SKImage.FromPixelCopy(CanvasRenderer.Info(raster.Width, raster.Height), (IntPtr)p, raster.Width * 4);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new IOException("PNG encoding failed.");
            using var file = File.Create(path); encoded.SaveTo(file); file.Flush(true);
        }
    }

    public static void Export(Document document, string path, bool jpeg, int quality = 92)
    {
        if (quality is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(quality));
        string destination = Path.GetFullPath(path);
        ProjectStore.CheckNoLinks(destination);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var renderer = new CanvasRenderer();
            using var image = renderer.Flatten(document, whiteBackground: jpeg);
            using var data = image.Encode(jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, quality)
                ?? throw new IOException("Image encoding failed.");
            byte[] encoded = data.ToArray();
            encoded = jpeg ? JpegResolution(encoded, document.Resolution) : PngResolution(encoded, document.Resolution);
            using (var output = File.Create(temporary)) { output.Write(encoded); output.Flush(true); }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static byte[] PngResolution(byte[] png, double dpi)
    {
        // Insert the standard pHYs chunk immediately after IHDR.
        byte[] chunk = new byte[21]; BinaryPrimitives.WriteInt32BigEndian(chunk, 9);
        "pHYs"u8.CopyTo(chunk.AsSpan(4)); uint ppm = (uint)Math.Round(dpi / .0254);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8), ppm);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(12), ppm); chunk[16] = 1;
        uint crc = 0xffffffff;
        foreach (byte b in chunk.AsSpan(4, 13)) { crc ^= b; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(17), ~crc);
        return [.. png.AsSpan(0, 33), .. chunk, .. png.AsSpan(33)];
    }
    private static byte[] JpegResolution(byte[] jpeg, double dpi)
    {
        // Skia emits JFIF. Preserve its thumbnail and other metadata.
        if (jpeg.Length > 18 && jpeg[2] == 0xff && jpeg[3] == 0xe0 && jpeg.AsSpan(6, 5).SequenceEqual("JFIF\0"u8))
        {
            jpeg[13] = 1; ushort value = (ushort)Math.Clamp(Math.Round(dpi), 1, 65535);
            BinaryPrimitives.WriteUInt16BigEndian(jpeg.AsSpan(14), value);
            BinaryPrimitives.WriteUInt16BigEndian(jpeg.AsSpan(16), value);
        }
        return jpeg;
    }
}
