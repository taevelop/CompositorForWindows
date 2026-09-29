namespace Compositor.Core;

/// <summary>Mac ExposureSettings: stops and offset in linear light, followed by gamma correction.</summary>
public sealed record ExposureAdjustment(double Exposure = 0, double Offset = 0, double Gamma = 1)
{
    public void Validate()
    {
        if (!double.IsFinite(Exposure) || !double.IsFinite(Offset) || !double.IsFinite(Gamma) ||
            Exposure is < -20 or > 20 || Offset is < -.5 or > .5 || Gamma is < .01 or > 9.99)
            throw new InvalidDataException("Exposure must be -20 to 20 stops, offset -0.5 to 0.5, and gamma 0.01 to 9.99.");
    }
    public bool IsIdentity => Exposure == 0 && Offset == 0 && Gamma == 1;
}
