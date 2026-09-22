using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

public class OrientationTests
{
    [Theory]
    [InlineData(1, new[] { 0, 1, 2, 3, 4, 5 })]
    [InlineData(2, new[] { 2, 1, 0, 5, 4, 3 })]
    [InlineData(3, new[] { 5, 4, 3, 2, 1, 0 })]
    [InlineData(4, new[] { 3, 4, 5, 0, 1, 2 })]
    [InlineData(5, new[] { 0, 3, 1, 4, 2, 5 })]
    [InlineData(6, new[] { 3, 0, 4, 1, 5, 2 })]
    [InlineData(7, new[] { 5, 2, 4, 1, 3, 0 })]
    [InlineData(8, new[] { 2, 5, 1, 4, 0, 3 })]
    public void JpegExifOrientationUsesTheCorrectPixelOrder(int orientation, int[] order)
    {
        string root = Path.Combine(Path.GetTempPath(), "CompositorOrientation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var bitmap = new SKBitmap(CanvasRenderer.Info(3, 2));
            byte[] pixels = [0, 0, 0, 255, 40, 40, 40, 255, 80, 80, 80, 255, 120, 120, 120, 255, 180, 180, 180, 255, 240, 240, 240, 255];
            Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
            using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            byte[] jpeg = encoded.ToArray(); string plain = Path.Combine(root, "plain.jpg"); File.WriteAllBytes(plain, jpeg);
            byte[] expected = ImageCodec.Load(plain).ToRgba();
            byte[] app1 = [0xff, 0xe1, 0, 34, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
                (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0, 0, 0, 0, 0];
            string oriented = Path.Combine(root, "oriented.jpg"); File.WriteAllBytes(oriented, [.. jpeg.AsSpan(0, 2), .. app1, .. jpeg.AsSpan(2)]);
            var raster = ImageCodec.Load(oriented); Assert.Equal(orientation >= 5 ? 2 : 3, raster.Width);
            var actual = raster.ToRgba();
            for (int i = 0; i < 6; i++) Assert.Equal(expected.AsSpan(order[i] * 4, 4).ToArray(), actual.AsSpan(i * 4, 4).ToArray());
        }
        finally
        {
            string full = Path.GetFullPath(root), temp = Path.GetFullPath(Path.GetTempPath());
            if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("CompositorOrientation-")) throw new IOException("Unsafe cleanup.");
            Directory.Delete(full, true);
        }
    }
}
