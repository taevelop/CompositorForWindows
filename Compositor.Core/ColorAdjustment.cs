namespace Compositor.Core;

/// <summary>Relative changes in [-100, 100]; zero leaves the image unchanged.</summary>
public readonly record struct ColorAdjustment(double Brightness = 0, double Contrast = 0, double Saturation = 0)
{
    public void Validate()
    {
        if (!double.IsFinite(Brightness) || !double.IsFinite(Contrast) || !double.IsFinite(Saturation) ||
            Math.Abs(Brightness) > 100 || Math.Abs(Contrast) > 100 || Math.Abs(Saturation) > 100)
            throw new ArgumentOutOfRangeException(nameof(ColorAdjustment), "Adjustments must be between -100 and 100.");
    }
}
