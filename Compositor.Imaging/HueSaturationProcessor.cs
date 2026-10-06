using Compositor.Core;

namespace Compositor.Imaging;

/// <summary>Ports HueSaturation.swift cube generation. CPU trilinear sampling replaces CIColorCube.</summary>
public static class HueSaturationProcessor
{
    public const int Dimension = 33;
    private readonly record struct Response(double Hue, double Saturation, double Lightness);
    private static Response[] Responses(HueSaturationAdjustment settings)
    {
        var result = new Response[361];
        for (int degree = 0; degree <= 360; degree++)
        {
            double h = 0, s = 0, l = 0;
            foreach (var (range, adjustment) in settings.Adjustments.OrderBy(p => p.Key))
            {
                double w = settings.Weight(range, degree);
                if (w <= 0) continue;
                h += adjustment.Hue * w; s += adjustment.Saturation * w; l += adjustment.Lightness * w;
            }
            result[degree] = new(h, s, l);
        }
        return result;
    }
    public static (double Hue, double Saturation, double Lightness) ToHsl(double red, double green, double blue)
    {
        double high = Math.Max(red, Math.Max(green, blue)), low = Math.Min(red, Math.Min(green, blue));
        double lightness = (high + low) / 2, delta = high - low;
        if (delta <= 0) return (0, 0, lightness);
        double saturation = delta / (1 - Math.Abs(2 * lightness - 1));
        double hue = high == red ? (green - blue) / delta : high == green ? (blue - red) / delta + 2 : (red - green) / delta + 4;
        return (HueBand.Wrap(hue * 60), Math.Min(1, saturation), lightness);
    }
    public static (double Red, double Green, double Blue) ToRgb(double hue, double saturation, double lightness)
    {
        if (saturation <= 0) return (lightness, lightness, lightness);
        double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation, sector = hue / 60;
        double second = chroma * (1 - Math.Abs(sector % 2 - 1)), offset = lightness - chroma / 2;
        (double r, double g, double b) = (int)sector switch
        {
            0 => (chroma, second, 0d), 1 => (second, chroma, 0d), 2 => (0d, chroma, second),
            3 => (0d, second, chroma), 4 => (second, 0d, chroma), _ => (chroma, 0d, second)
        };
        return (Math.Clamp(r + offset, 0, 1), Math.Clamp(g + offset, 0, 1), Math.Clamp(b + offset, 0, 1));
    }
    private static (double Red, double Green, double Blue) Adjust(double red, double green, double blue, HueSaturationAdjustment settings, Response[] responses)
    {
        var (h, s, l) = ToHsl(red, green, blue); double amount;
        if (settings.Colorize)
        {
            var chosen = settings.Adjustment(settings.Range);
            h = chosen.Hue % 360; s = Math.Clamp(chosen.Saturation / 100, 0, 1); amount = chosen.Lightness / 100;
        }
        else
        {
            var response = responses[Math.Clamp((int)Math.Round(h, MidpointRounding.AwayFromZero), 0, 360)];
            h = HueBand.Wrap(h + response.Hue); s = Math.Clamp(s * (1 + response.Saturation / 100), 0, 1); amount = response.Lightness / 100;
        }
        amount = Math.Clamp(amount, -1, 1);
        l = amount >= 0 ? l + (1 - l) * amount : l * (1 + amount);
        return ToRgb(h, s, Math.Clamp(l, 0, 1));
    }
    public static float[] Cube(HueSaturationAdjustment settings)
    {
        settings.Validate(); var response = Responses(settings); var cube = new float[Dimension * Dimension * Dimension * 4]; int p = 0;
        for (int b = 0; b < Dimension; b++) for (int g = 0; g < Dimension; g++) for (int r = 0; r < Dimension; r++)
        {
            var color = Adjust(r / 32d, g / 32d, b / 32d, settings, response);
            cube[p++] = (float)color.Red; cube[p++] = (float)color.Green; cube[p++] = (float)color.Blue; cube[p++] = 1;
        }
        return cube;
    }
    public static void Apply(byte[] pixels, HueSaturationAdjustment settings)
    {
        settings.Validate();
        if (pixels.Length % 4 != 0) throw new ArgumentException("Expected RGBA pixels.", nameof(pixels));
        if (settings.IsIdentity) return;
        NativePixels.HueCube(pixels, pixels.Length / 4, Cube(settings));
    }
}
