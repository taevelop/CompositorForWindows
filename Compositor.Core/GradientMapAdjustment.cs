namespace Compositor.Core;

public sealed record AdjustmentColor(double Red, double Green, double Blue)
{
    public void Validate()
    {
        if (new[] { Red, Green, Blue }.Any(v => !double.IsFinite(v) || v < 0 || v > 1))
            throw new InvalidDataException("Adjustment colors must be finite values from 0 to 1.");
    }
}
public sealed record GradientMapAdjustment
{
    public AdjustmentColor Shadows { get; init; } = new(0, 0, 0);
    public AdjustmentColor Highlights { get; init; } = new(1, 1, 1);
    public bool Reversed { get; init; }
    public void Validate()
    {
        if (Shadows is null || Highlights is null) throw new InvalidDataException("Missing gradient colors.");
        Shadows.Validate(); Highlights.Validate();
    }
    public byte[] Table()
    {
        Validate();
        var dark = Reversed ? Highlights : Shadows;
        var light = Reversed ? Shadows : Highlights;
        double[] from = [dark.Red, dark.Green, dark.Blue], to = [light.Red, light.Green, light.Blue];
        var result = new byte[768];
        for (int i = 0; i < 256; i++)
            for (int c = 0; c < 3; c++)
                result[i * 3 + c] = (byte)Math.Clamp(Math.Round((from[c] + (to[c] - from[c]) * (i / 255d)) * 255, MidpointRounding.AwayFromZero), 0, 255);
        return result;
    }
}
