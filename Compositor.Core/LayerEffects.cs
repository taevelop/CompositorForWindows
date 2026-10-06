namespace Compositor.Core;

public sealed record ColorOverlayEffect(double Red = 0, double Green = 0, double Blue = 0, double Opacity = 1, bool? Enabled = null)
{
    public bool IsEnabled => Enabled != false;
    public void Validate()
    {
        if (!double.IsFinite(Red) || !double.IsFinite(Green) || !double.IsFinite(Blue) || !double.IsFinite(Opacity) ||
            Red is < 0 or > 1 || Green is < 0 or > 1 || Blue is < 0 or > 1 || Opacity is < 0 or > 1)
            throw new InvalidDataException("Overlay RGB and opacity must be finite numbers from 0 to 1.");
    }
}

public sealed record ShadowEffect(double Angle = 90, double Distance = 20, double Blur = 20,
    double Red = 0, double Green = 0, double Blue = 0, double Opacity = .5, bool? Enabled = null)
{
    public bool IsEnabled => Enabled != false;
    public double OffsetX => -Math.Cos(Angle * Math.PI / 180) * Distance;
    public double OffsetY => Math.Sin(Angle * Math.PI / 180) * Distance;
    public void Validate()
    {
        new ColorOverlayEffect(Red, Green, Blue, Opacity).Validate();
        if (!double.IsFinite(Angle) || !double.IsFinite(Distance) || !double.IsFinite(Blur) ||
            Angle is < -360 or > 360 || Distance is < 0 or > 5000 || Blur is < 0 or > 500)
            throw new InvalidDataException("Shadow angle must be -360..360, distance 0..5000 and blur 0..500.");
    }
}

public sealed record StrokeEffect(double Size = 4, double Red = 0, double Green = 0, double Blue = 0,
    double Opacity = 1, bool Inside = false, bool? Enabled = null)
{
    public bool IsEnabled => Enabled != false;
    public void Validate()
    {
        new ColorOverlayEffect(Red, Green, Blue, Opacity).Validate();
        if (!double.IsFinite(Size) || Size is < 0 or > 500) throw new InvalidDataException("Stroke size must be 0..500.");
    }
}

public sealed record LayerEffects(ColorOverlayEffect? ColorOverlay = null, ShadowEffect? Shadow = null, StrokeEffect? Stroke = null)
{
    public void Validate() { ColorOverlay?.Validate(); Shadow?.Validate(); Stroke?.Validate(); }
}
