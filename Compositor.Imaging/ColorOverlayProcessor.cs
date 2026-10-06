using Compositor.Core;

namespace Compositor.Imaging;

internal static class ColorOverlayProcessor
{
    // Mac LayerEffects: source-over color through the already masked source alpha.
    // Unlike color adjustments, this intentionally increases partially transparent alpha.
    public static void Apply(Span<byte> pixels, ColorOverlayEffect overlay)
    {
        overlay.Validate(); if (!overlay.IsEnabled || overlay.Opacity == 0) return;
        ApplyLookup(pixels, Lookup(overlay));
    }
    public static byte[] Lookup(ColorOverlayEffect overlay)
    {
        overlay.Validate(); var lookup = new byte[3 * 65536 + 256];
        double[] colors = [overlay.Red * 255, overlay.Green * 255, overlay.Blue * 255];
        byte Round(double value) => (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
        for (int alpha = 0; alpha < 256; alpha++)
        {
            double coverage = overlay.IsEnabled ? alpha / 255.0 * overlay.Opacity : 0, keep = 1 - coverage;
            lookup[3 * 65536 + alpha] = Round(255 * coverage + alpha * keep);
            for (int channel = 0; channel < 3; channel++) for (int value = 0; value < 256; value++)
                lookup[channel * 65536 + alpha * 256 + value] = Round(colors[channel] * coverage + value * keep);
        }
        return lookup;
    }
    public static void ApplyLookup(Span<byte> pixels, byte[] lookup)
    {
        for (int p = 0; p < pixels.Length; p += 4)
        {
            int alpha = pixels[p + 3], offset = alpha * 256;
            pixels[p] = lookup[offset + pixels[p]];
            pixels[p + 1] = lookup[65536 + offset + pixels[p + 1]];
            pixels[p + 2] = lookup[131072 + offset + pixels[p + 2]];
            pixels[p + 3] = lookup[196608 + alpha];
        }
    }
}
