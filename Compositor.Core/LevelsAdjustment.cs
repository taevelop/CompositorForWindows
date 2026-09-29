namespace Compositor.Core;

public enum LevelsChannel { RGB, Red, Green, Blue }

public sealed record LevelRange(double Black = 0, double Gamma = 1, double White = 255, double OutputBlack = 0, double OutputWhite = 255)
{
    public void Validate()
    {
        if (!double.IsFinite(Black) || !double.IsFinite(White) || !double.IsFinite(Gamma) || !double.IsFinite(OutputBlack) || !double.IsFinite(OutputWhite) ||
            Black is < 0 or > 254 || White < Black + 1 || White > 255 || Gamma is < .1 or > 9.99 || OutputBlack is < 0 or > 255 || OutputWhite is < 0 or > 255)
            throw new InvalidDataException("Input black must be 0–254, white at least black + 1 and at most 255, gamma 0.1–9.99, and outputs 0–255.");
    }
    public double Apply(double value) => (OutputBlack + Math.Pow(Math.Clamp((value * 255 - Black) / (White - Black), 0, 1), 1 / Gamma) * (OutputWhite - OutputBlack)) / 255;
}

/// <summary>Mac LevelsSettings: per-channel ranges first, then the RGB range; channel is the editor selection.</summary>
public sealed record LevelsAdjustment
{
    public LevelsChannel Channel { get; init; } = LevelsChannel.RGB;
    public LevelRange RGB { get; init; } = new();
    public LevelRange Red { get; init; } = new();
    public LevelRange Green { get; init; } = new();
    public LevelRange Blue { get; init; } = new();
    public LevelRange Range(LevelsChannel channel) => channel switch
    {
        LevelsChannel.RGB => RGB, LevelsChannel.Red => Red, LevelsChannel.Green => Green, LevelsChannel.Blue => Blue,
        _ => throw new InvalidDataException("Unknown Levels channel.")
    };
    public LevelsAdjustment WithRange(LevelsChannel channel, LevelRange range) => channel switch
    {
        LevelsChannel.RGB => this with { RGB = range }, LevelsChannel.Red => this with { Red = range },
        LevelsChannel.Green => this with { Green = range }, LevelsChannel.Blue => this with { Blue = range },
        _ => throw new InvalidDataException("Unknown Levels channel.")
    };
    public bool IsIdentity => RGB == new LevelRange() && Red == new LevelRange() && Green == new LevelRange() && Blue == new LevelRange();
    public void Validate()
    {
        if (!Enum.IsDefined(Channel) || RGB is null || Red is null || Green is null || Blue is null) throw new InvalidDataException("Invalid Levels settings.");
        RGB.Validate(); Red.Validate(); Green.Validate(); Blue.Validate();
    }
}
