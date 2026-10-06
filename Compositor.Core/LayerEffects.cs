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

public sealed record LayerEffects(ColorOverlayEffect? ColorOverlay = null)
{
    public void Validate() => ColorOverlay?.Validate();
}
