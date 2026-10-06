using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

/// <summary>Square min/max morphology matching the original stroke definition, linear in pixel count.</summary>
public static class StrokeProcessor
{
    public static byte[] Coverage(ReadOnlySpan<byte> alpha, int width, int height, int radius, bool inside)
    {
        if (width < 1 || height < 1 || alpha.Length != checked(width * height) || radius < 1 || radius > 500)
            throw new ArgumentException("Invalid stroke coverage input.");
        var source = alpha.ToArray(); var pass = new byte[source.Length]; var result = new byte[source.Length];
        var queue = new int[Math.Max(width, height)];
        void Sweep(byte[] input, byte[] output, int lines, int count, int lineStep, int elementStep)
        {
            for (int line = 0; line < lines; line++)
            {
                int start = line * lineStep, head = 0, tail = 0, next = 0;
                for (int center = 0; center < count; center++)
                {
                    while (next <= Math.Min(count - 1, center + radius))
                    {
                        byte value = input[start + next * elementStep];
                        while (tail > head && (inside ? input[start + queue[tail - 1] * elementStep] >= value : input[start + queue[tail - 1] * elementStep] <= value)) tail--;
                        queue[tail++] = next++;
                    }
                    while (head < tail && queue[head] < center - radius) head++;
                    output[start + center * elementStep] = inside && (center < radius || center + radius >= count) ? (byte)0 : input[start + queue[head] * elementStep];
                }
            }
        }
        Sweep(source, pass, height, width, width, 1);
        Sweep(pass, result, width, height, 1, width);
        for (int i = 0; i < result.Length; i++) result[i] = (byte)Math.Max(0, inside ? source[i] - result[i] : result[i] - source[i]);
        return result;
    }

    public static SKImage Render(SKImage source, StrokeEffect effect, out int inset)
    {
        effect.Validate();
        inset = Math.Max(1, (int)Math.Round(effect.Size, MidpointRounding.AwayFromZero));
        int width = checked(source.Width + inset * 2), height = checked(source.Height + inset * 2);
        using var original = new SKBitmap(CanvasRenderer.Info(source.Width, source.Height));
        if (!source.ReadPixels(original.Info, original.GetPixels(), original.RowBytes, 0, 0)) throw new IOException("Cannot read stroke source.");
        var bytes = original.GetPixelSpan(); var alpha = new byte[checked(width * height)];
        for (int y = 0; y < source.Height; y++) for (int x = 0; x < source.Width; x++)
            alpha[(y + inset) * width + x + inset] = bytes[(y * source.Width + x) * 4 + 3];
        var ring = Coverage(alpha, width, height, inset, effect.Inside);
        using var bitmap = new SKBitmap(CanvasRenderer.Info(width, height)); var output = bitmap.GetPixelSpan();
        for (int i = 0; i < ring.Length; i++)
        {
            int a = (int)Math.Round(ring[i] * effect.Opacity);
            output[i * 4] = (byte)Math.Round(a * effect.Red); output[i * 4 + 1] = (byte)Math.Round(a * effect.Green);
            output[i * 4 + 2] = (byte)Math.Round(a * effect.Blue); output[i * 4 + 3] = (byte)a;
        }
        bitmap.SetImmutable(); return SKImage.FromBitmap(bitmap);
    }
}
