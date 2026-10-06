using System.Collections.Immutable;

namespace Compositor.Core;

public enum HueRange { Master, Reds, Yellows, Greens, Cyans, Blues, Magentas }
public sealed record HueRangeAdjustment(double Hue = 0, double Saturation = 0, double Lightness = 0)
{
    public void Validate()
    {
        if (!double.IsFinite(Hue) || Math.Abs(Hue) > 360 ||
            !double.IsFinite(Saturation) || Math.Abs(Saturation) > 100 ||
            !double.IsFinite(Lightness) || Math.Abs(Lightness) > 100)
            throw new InvalidDataException("Invalid Hue/Saturation range adjustment.");
    }
}
public sealed record HueBand(double FalloffStart, double RangeStart, double RangeEnd, double FalloffEnd)
{
    public double[] Handles => [FalloffStart, RangeStart, RangeEnd, FalloffEnd];
    public static double Wrap(double value) { var r = value % 360; return r < 0 ? r + 360 : r; }
    public static double Forward(double from, double to) => Wrap(to - from);
    public static HueBand Default(HueRange range)
    {
        if (!Enum.IsDefined(range)) throw new ArgumentOutOfRangeException(nameof(range));
        if (range == HueRange.Master) return new(0, 0, 360, 360);
        double center = ((int)range - 1) * 60;
        return new(Wrap(center - 45), Wrap(center - 15), Wrap(center + 15), Wrap(center + 45));
    }
    public void Validate()
    {
        if (Handles.Any(v => !double.IsFinite(v)) ||
            Handles.Any(v => !double.IsFinite(v - FalloffStart)))
            throw new InvalidDataException("Invalid hue band.");
    }
    public double Weight(double hue)
    {
        double span = Forward(FalloffStart, FalloffEnd);
        if (span <= 0) return 1;
        double position = Forward(FalloffStart, hue);
        if (position > span) return 0;
        double rampIn = Forward(FalloffStart, RangeStart), plateauEnd = Forward(FalloffStart, RangeEnd);
        if (position < rampIn) return rampIn > 0 ? position / rampIn : 1;
        if (position <= plateauEnd) return 1;
        double rampOut = span - plateauEnd;
        return rampOut > 0 ? (span - position) / rampOut : 1;
    }
    public HueBand MoveHandle(int index, double degrees)
    {
        if (index is < 0 or > 3 || !double.IsFinite(degrees)) throw new ArgumentOutOfRangeException();
        var v = Handles; v[index] = Wrap(degrees);
        double span = Forward(v[0], v[3]), start = Forward(v[0], v[1]), end = Forward(v[0], v[2]);
        return span > 1 && span <= 350 && start <= end && end <= span ? new(v[0], v[1], v[2], v[3]) : this;
    }
    public HueBand Centered(double hue)
    {
        if (!double.IsFinite(hue)) throw new ArgumentOutOfRangeException(nameof(hue));
        double core = Forward(RangeStart, RangeEnd), leading = Forward(FalloffStart, RangeStart), trailing = Forward(RangeEnd, FalloffEnd);
        double start = Wrap(hue - core / 2);
        return new(Wrap(start - leading), start, Wrap(start + core), Wrap(start + core + trailing));
    }
    private HueBand Normalize()
    {
        var result = new HueBand(Wrap(FalloffStart), Wrap(RangeStart), Wrap(RangeEnd), Wrap(FalloffEnd));
        return Forward(result.FalloffStart, result.FalloffEnd) > 350 ? result with { FalloffEnd = Wrap(result.FalloffStart + 350) } : result;
    }
    public HueBand Include(double hue)
    {
        if (!double.IsFinite(hue)) throw new ArgumentOutOfRangeException(nameof(hue));
        if (Weight(hue) >= 1) return this;
        double leading = Forward(FalloffStart, RangeStart), trailing = Forward(RangeEnd, FalloffEnd);
        return (Forward(hue, RangeStart) <= Forward(RangeEnd, hue)
            ? this with { RangeStart = hue, FalloffStart = hue - leading }
            : this with { RangeEnd = hue, FalloffEnd = hue + trailing }).Normalize();
    }
    public HueBand Exclude(double hue)
    {
        if (!double.IsFinite(hue)) throw new ArgumentOutOfRangeException(nameof(hue));
        if (Weight(hue) <= 0) return this;
        double leading = Forward(FalloffStart, RangeStart), trailing = Forward(RangeEnd, FalloffEnd);
        return (Forward(FalloffStart, hue) <= Forward(hue, FalloffEnd)
            ? this with { FalloffStart = hue + 1, RangeStart = hue + 1 + leading }
            : this with { FalloffEnd = hue - 1, RangeEnd = hue - 1 - trailing }).Normalize();
    }
}
public sealed record HueSaturationAdjustment
{
    public HueRange Range { get; init; }
    public bool Colorize { get; init; }
    public bool InvertRange { get; init; }
    public ImmutableDictionary<HueRange, HueRangeAdjustment> Adjustments { get; init; } =
        ImmutableDictionary<HueRange, HueRangeAdjustment>.Empty.Add(HueRange.Master, new());
    public ImmutableDictionary<HueRange, HueBand> Bands { get; init; } =
        Enum.GetValues<HueRange>().ToImmutableDictionary(r => r, HueBand.Default);
    public static HueSaturationAdjustment ColorizeStart => new()
    {
        Colorize = true, Adjustments = ImmutableDictionary<HueRange, HueRangeAdjustment>.Empty.Add(HueRange.Master, new(Saturation: 25))
    };
    public HueRangeAdjustment Adjustment(HueRange range) => Adjustments.GetValueOrDefault(range) ?? new();
    public HueBand Band(HueRange range) => Bands.GetValueOrDefault(range) ?? HueBand.Default(range);
    public bool IsIdentity => !Colorize && Adjustments.Values.All(v => v == new HueRangeAdjustment());
    public double Weight(HueRange range, double hue)
    {
        if (range == HueRange.Master) return 1;
        double weight = Band(range).Weight(hue);
        return InvertRange && Range == range ? 1 - weight : weight;
    }
    public void Validate()
    {
        if (!Enum.IsDefined(Range) || Adjustments is null || Bands is null) throw new InvalidDataException("Invalid Hue/Saturation settings.");
        foreach (var (range, value) in Adjustments) { if (!Enum.IsDefined(range) || value is null) throw new InvalidDataException("Invalid hue range."); value.Validate(); }
        foreach (var (range, value) in Bands) { if (!Enum.IsDefined(range) || value is null) throw new InvalidDataException("Invalid hue band."); value.Validate(); }
    }
    public bool Equals(HueSaturationAdjustment? other) => other is not null && Range == other.Range && Colorize == other.Colorize && InvertRange == other.InvertRange
        && Adjustments.Count == other.Adjustments.Count && Bands.Count == other.Bands.Count
        && Adjustments.All(p => other.Adjustments.TryGetValue(p.Key, out var value) && value == p.Value)
        && Bands.All(p => other.Bands.TryGetValue(p.Key, out var value) && value == p.Value);
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Range); hash.Add(Colorize); hash.Add(InvertRange);
        foreach (var pair in Adjustments.OrderBy(p => p.Key)) { hash.Add(pair.Key); hash.Add(pair.Value); }
        foreach (var pair in Bands.OrderBy(p => p.Key)) { hash.Add(pair.Key); hash.Add(pair.Value); }
        return hash.ToHashCode();
    }
}
