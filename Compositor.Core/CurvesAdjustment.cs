using System.Collections.Immutable;

namespace Compositor.Core;

public readonly record struct CurvePoint(double X, double Y);

/// <summary>Immutable, value-equal handles with the Mac cubic Hermite interpolation.</summary>
public sealed class ToneCurve : IEquatable<ToneCurve>
{
    public static ToneCurve Identity { get; } = new(new(0, 0), new(255, 255));
    public ImmutableArray<CurvePoint> Points { get; }
    public ToneCurve(params CurvePoint[] points)
    {
        if (points is null || points.Length is < 2 or > 32 || points[0].X != 0 || points[^1].X != 255)
            throw new InvalidDataException("A curve needs 2 to 32 points with endpoints at input 0 and 255.");
        for (int i = 0; i < points.Length; i++)
            if (!double.IsFinite(points[i].X) || !double.IsFinite(points[i].Y) || points[i].X is < 0 or > 255 || points[i].Y is < 0 or > 255 || (i > 0 && points[i].X <= points[i - 1].X))
                throw new InvalidDataException("Curve inputs must increase strictly; input and output must be finite values from 0 to 255.");
        for (int i = 1; i < points.Length; i++)
            if (!double.IsFinite((points[i].Y - points[i - 1].Y) / (points[i].X - points[i - 1].X)))
                throw new InvalidDataException("Curve inputs are too close to interpolate safely.");
        Points = ImmutableArray.CreateRange(points);
    }
    public bool IsIdentity => Points.All(p => p.X == p.Y);
    public double Value(double x)
    {
        int i = 0; while (i < Points.Length - 2 && Points[i + 1].X <= x) i++;
        double Secant(int j) => (Points[j + 1].Y - Points[j].Y) / (Points[j + 1].X - Points[j].X);
        double Slope(int j)
        {
            if (j == 0) return Secant(0);
            if (j == Points.Length - 1) return Secant(j - 1);
            double left = Secant(j - 1), right = Secant(j);
            return left * right <= 0 ? 0 : 2 / (1 / left + 1 / right);
        }
        double h = Points[i + 1].X - Points[i].X, t = Math.Clamp((x - Points[i].X) / h, 0, 1);
        return Math.Clamp((2*t*t*t-3*t*t+1)*Points[i].Y + (t*t*t-2*t*t+t)*h*Slope(i)
            + (-2*t*t*t+3*t*t)*Points[i+1].Y + (t*t*t-t*t)*h*Slope(i+1), 0, 255);
    }
    public bool Equals(ToneCurve? other) => other is not null && Points.SequenceEqual(other.Points);
    public override bool Equals(object? obj) => obj is ToneCurve other && Equals(other);
    public override int GetHashCode() { var hash = new HashCode(); foreach (var p in Points) hash.Add(p); return hash.ToHashCode(); }
}

public sealed record CurvesAdjustment
{
    public LevelsChannel Channel { get; init; } = LevelsChannel.RGB;
    public ToneCurve RGB { get; init; } = ToneCurve.Identity;
    public ToneCurve Red { get; init; } = ToneCurve.Identity;
    public ToneCurve Green { get; init; } = ToneCurve.Identity;
    public ToneCurve Blue { get; init; } = ToneCurve.Identity;
    public ToneCurve Curve(LevelsChannel channel) => channel switch
    {
        LevelsChannel.RGB => RGB, LevelsChannel.Red => Red, LevelsChannel.Green => Green, LevelsChannel.Blue => Blue,
        _ => throw new InvalidDataException("Unknown Curves channel.")
    };
    public CurvesAdjustment WithCurve(LevelsChannel channel, ToneCurve curve) => channel switch
    {
        LevelsChannel.RGB => this with { RGB = curve }, LevelsChannel.Red => this with { Red = curve },
        LevelsChannel.Green => this with { Green = curve }, LevelsChannel.Blue => this with { Blue = curve },
        _ => throw new InvalidDataException("Unknown Curves channel.")
    };
    public bool IsIdentity => RGB.IsIdentity && Red.IsIdentity && Green.IsIdentity && Blue.IsIdentity;
    public void Validate()
    {
        if (!Enum.IsDefined(Channel) || RGB is null || Red is null || Green is null || Blue is null)
            throw new InvalidDataException("Invalid Curves settings.");
    }
}
