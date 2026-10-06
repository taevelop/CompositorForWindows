namespace Compositor.Core;

/// <summary>Preserves legacy scalar fields separately from optional range-aware settings.</summary>
public sealed record HueSaturationLayerAdjustment(double Hue = 0, double Saturation = 0,
    double Lightness = 0, bool Colorize = false, HueSaturationAdjustment? Settings = null)
{
    public HueSaturationAdjustment Resolved => Settings ?? new HueSaturationAdjustment
    {
        Colorize = Colorize,
        Adjustments = System.Collections.Immutable.ImmutableDictionary<HueRange, HueRangeAdjustment>.Empty.Add(HueRange.Master, new(Hue, Saturation, Lightness))
    };
    public bool IsIdentity => Resolved.IsIdentity;
    public void Validate()
    {
        new HueRangeAdjustment(Hue, Saturation, Lightness).Validate();
        Settings?.Validate();
    }
}
