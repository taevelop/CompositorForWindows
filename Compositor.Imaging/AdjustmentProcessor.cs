using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

internal static class AdjustmentProcessor
{
    internal static float[] Tables(ExposureAdjustment settings)
    {
        settings.Validate(); var table = new float[768]; double scale = Math.Pow(2, settings.Exposure);
        for (int i = 0; i < 256; i++)
        {
            double encoded = i / 255.0;
            double linear = encoded <= .04045 ? encoded / 12.92 : Math.Pow((encoded + .055) / 1.055, 2.4);
            linear = Math.Pow(Math.Max(0, linear * scale + settings.Offset), 1 / settings.Gamma);
            double output = linear <= .0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - .055;
            table[i] = table[i + 256] = table[i + 512] = (float)Math.Clamp(output, 0, 1);
        }
        return table;
    }
    internal static float[] Tables(LevelsAdjustment settings)
    {
        settings.Validate(); var table = new float[768];
        for (int c = 0; c < 3; c++) for (int i = 0; i < 256; i++)
            table[c * 256 + i] = (float)settings.RGB.Apply(settings.Range((LevelsChannel)(c + 1)).Apply(i / 255.0));
        return table;
    }
    internal static float[] Tables(CurvesAdjustment settings)
    {
        settings.Validate(); var table = new float[768];
        for (int c = 0; c < 3; c++) for (int i = 0; i < 256; i++)
            table[c * 256 + i] = (float)(settings.RGB.Value(settings.Curve((LevelsChannel)(c + 1)).Value(i)) / 255);
        return table;
    }
    public static void Apply(SKBitmap bitmap, Document document, Layer layer, double opacity)
    {
        bool identity = layer.Exposure?.IsIdentity ?? layer.Levels?.IsIdentity ?? layer.Curves?.IsIdentity ?? throw new InvalidOperationException("No adjustment settings.");
        if (identity && layer.Blend == BlendMode.Normal) return;
        var tables = layer.Exposure is { } exposure ? Tables(exposure) : layer.Levels is { } levels ? Tables(levels) : Tables(layer.Curves!);
        var original = bitmap.GetPixelSpan();
        // Evaluate the original Mac C kernel once for each possible alpha/channel pair.
        // The image then uses exact byte lookups rather than millions of floating-point LUT interpolations.
        var samples = new byte[256 * 256 * 4];
        for (int alpha = 0; alpha < 256; alpha++) for (int value = 0; value < 256; value++)
        {
            int p = (alpha * 256 + value) * 4;
            samples[p] = samples[p + 1] = samples[p + 2] = (byte)value; samples[p + 3] = (byte)alpha;
        }
        NativePixels.Levels(samples, 256 * 256, tables);
        var lookup = new byte[3 * 256 * 256];
        for (int c = 0; c < 3; c++) for (int i = 0; i < 256 * 256; i++) lookup[c * 65536 + i] = samples[i * 4 + c];
        var t = layer.Transform;
        bool uniform = layer.Mask is { Enabled: true, Pixels.Width: 1, Pixels.Height: 1 } &&
            t.X == 0 && t.Y == 0 && t.Width == document.Width && t.Height == document.Height && t.Rotation % 360 == 0;
        if (uniform) opacity *= layer.Mask!.Pixels.Tiles[new(0, 0)].Bytes[0] / 255.0;
        using var coverage = uniform ? null : MaskCoverage(document, layer);
        ReadOnlySpan<byte> mask = coverage is null ? default : coverage.GetPixelSpan();
        for (int p = 0; p < original.Length; p += 4)
        {
            int alpha = original[p + 3]; if (alpha == 0) continue;
            double weight = opacity * (coverage is null ? 1 : mask[p] / 255.0);
            if (weight == 0) continue;
            if (layer.Blend == BlendMode.Normal)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    int before = original[p + channel], after = lookup[channel * 65536 + alpha * 256 + before];
                    original[p + channel] = weight == 1 ? (byte)after : (byte)Math.Round(before * (1 - weight) + after * weight, MidpointRounding.AwayFromZero);
                }
                continue;
            }
            for (int channel = 0; channel < 3; channel++)
            {
                double b = original[p + channel] / (double)alpha, s = lookup[channel * 65536 + alpha * 256 + original[p + channel]] / (double)alpha;
                double blend = layer.Blend switch
                {
                    BlendMode.Normal => s, BlendMode.Multiply => b * s, BlendMode.Screen => b + s - b * s,
                    BlendMode.Overlay => b <= .5 ? 2 * b * s : 1 - 2 * (1 - b) * (1 - s),
                    BlendMode.Darken => Math.Min(b, s), BlendMode.Lighten => Math.Max(b, s), BlendMode.Difference => Math.Abs(b - s),
                    _ => throw new NotSupportedException("Unsupported adjustment blend mode.")
                };
                original[p + channel] = (byte)Math.Clamp(Math.Round(original[p + channel] * (1 - weight) + blend * alpha * weight, MidpointRounding.AwayFromZero), 0, alpha);
            }
        }
    }
    private static SKBitmap? MaskCoverage(Document doc, Layer layer)
    {
        if (layer.Mask is not { Enabled: true } mask) return null;
        var bitmap = new SKBitmap(CanvasRenderer.Info(doc.Width, doc.Height));
        try
        {
            using var canvas = new SKCanvas(bitmap); canvas.Clear();
            using var renderer = new CanvasRenderer();
            var coverage = layer with { Pixels = mask.Pixels, Exposure = null, Levels = null, Curves = null, Effects = null, Mask = null, ParentId = null, Visible = true, Opacity = 1, Blend = BlendMode.Normal };
            renderer.Draw(canvas, doc with { Layers = [coverage] }); canvas.Flush(); return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
}
