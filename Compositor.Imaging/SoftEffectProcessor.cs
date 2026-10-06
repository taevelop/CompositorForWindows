using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;
public static class SoftEffectProcessor
{
    // Original definitions: inner=A*(1-shiftedBlur(A)); glow=blur(A)*(1-A).
    public static byte Coverage(byte shape, byte softened, bool inner) =>
        (byte)(inner ? (shape * (255 - softened) + 127) / 255 : (softened * (255 - shape) + 127) / 255);

    public static SKImage Render(SKImage source, double dx, double dy, double blur, double red, double green, double blue,
        double opacity, bool inner, out int inset)
    {
        inset = inner ? 0 : (int)Math.Ceiling(blur * 3);
        int w = checked(source.Width + inset * 2), h = checked(source.Height + inset * 2);
        using var original = new SKBitmap(CanvasRenderer.Info(source.Width, source.Height));
        if (!source.ReadPixels(original.Info, original.GetPixels(), original.RowBytes, 0, 0)) throw new IOException("Cannot read effect source.");
        using var softened = new SKBitmap(CanvasRenderer.Info(w, h));
        using (var canvas = new SKCanvas(softened))
        {
            canvas.Clear();
            using var filter = SKImageFilter.CreateDropShadowOnly((float)dx, (float)dy, (float)(blur / 2), (float)(blur / 2), SKColors.White);
            using var paint = new SKPaint { ImageFilter = filter };
            canvas.DrawImage(source, inset, inset, new SKSamplingOptions(SKFilterMode.Nearest), paint);
            canvas.Flush();
        }
        var originalBytes = original.GetPixelSpan(); var bytes = softened.GetPixelSpan();
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int sx = x - inset, sy = y - inset, p = (y * w + x) * 4;
            byte a = sx >= 0 && sy >= 0 && sx < source.Width && sy < source.Height ? originalBytes[(sy * source.Width + sx) * 4 + 3] : (byte)0;
            int coverage = (int)Math.Round(Coverage(a, bytes[p + 3], inner) * opacity);
            bytes[p] = (byte)Math.Round(coverage * red); bytes[p + 1] = (byte)Math.Round(coverage * green);
            bytes[p + 2] = (byte)Math.Round(coverage * blue); bytes[p + 3] = (byte)coverage;
        }
        softened.SetImmutable(); return SKImage.FromBitmap(softened);
    }
}
