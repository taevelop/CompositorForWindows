using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

public static class SelectionGeometry
{
    public static SKPath Path(DocumentSelection selection)
    {
        selection.Validate();
        if (selection.IsEmpty) return new();
        var path = SKPath.ParseSvgPathData(selection.PathData) ?? throw new InvalidDataException("Invalid selection SVG path.");
        var bounds=path.Bounds;
        if (!float.IsFinite(bounds.Left)||!float.IsFinite(bounds.Top)||!float.IsFinite(bounds.Right)||!float.IsFinite(bounds.Bottom)) { path.Dispose(); throw new InvalidDataException("Nonfinite selection path."); }
        using var builder = new SKPathBuilder(path);
        builder.FillType = selection.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
        path.Dispose();
        return builder.Detach();
    }
    private static DocumentSelection Snapshot(SKPath path, bool antialiased = true, double feather = 0)
    {
        var b = path.Bounds;
        if (!float.IsFinite(b.Left) || !float.IsFinite(b.Top) || !float.IsFinite(b.Right) || !float.IsFinite(b.Bottom))
            throw new InvalidDataException("Invalid selection coordinates.");
        var selection = new DocumentSelection(path.IsEmpty || b.Width <= 0 || b.Height <= 0 ? "" : path.ToSvgPathData(), antialiased, feather,
            path.FillType is SKPathFillType.EvenOdd or SKPathFillType.InverseEvenOdd);
        selection.Validate(); return selection;
    }
    private static SKPath CanvasPath(int width, int height)
    {
        Limits.CheckDimensions(width, height); using var b = new SKPathBuilder(); b.AddRect(new(0, 0, width, height)); return b.Detach();
    }
    private static SKPath Operate(SKPath a, SKPath b, SKPathOp operation) =>
        a.Op(b, operation) ?? throw new InvalidOperationException("Cannot combine selection outlines.");
    public static DocumentSelection Box(double x, double y, double width, double height, bool ellipse = false, bool antialiased = true)
    {
        if (new[] { x, y, width, height }.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1_000_000))
            throw new ArgumentOutOfRangeException("Invalid selection box.");
        var rect = new SKRect((float)Math.Min(x, x + width), (float)Math.Min(y, y + height), (float)Math.Max(x, x + width), (float)Math.Max(y, y + height));
        using var builder = new SKPathBuilder(); if (rect.Width > 0 && rect.Height > 0) { if (ellipse) builder.AddOval(rect); else builder.AddRect(rect); }
        using var path = builder.Detach(); return Snapshot(path, antialiased);
    }
    public static DocumentSelection Polygon(IEnumerable<PointD> points, bool antialiased = true)
    {
        var p = points.Take(100_001).ToArray();
        if (p.Length > 100_000 || p.Any(v => !double.IsFinite(v.X) || !double.IsFinite(v.Y) || Math.Abs(v.X) > 1_000_000 || Math.Abs(v.Y) > 1_000_000))
            throw new ArgumentException("Invalid selection polygon.");
        using var b = new SKPathBuilder();
        if (p.Length >= 3) b.AddPoly(p.Select(v => new SKPoint((float)v.X, (float)v.Y)).ToArray(), true);
        using var path = b.Detach(); return Snapshot(path, antialiased);
    }
    public static DocumentSelection? Combine(DocumentSelection? current, DocumentSelection shape, SelectionMode mode, int width, int height)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        using var path = Path(shape); using var canvas = CanvasPath(width, height); using var clipped = Operate(path, canvas, SKPathOp.Intersect);
        if (mode == SelectionMode.Subtract && current is null) return null;
        if (current is null || mode == SelectionMode.Replace) return Snapshot(clipped, shape.Antialiased);
        using var previous = Path(current);
        using var result = Operate(previous, clipped, mode == SelectionMode.Add ? SKPathOp.Union : SKPathOp.Difference);
        return Snapshot(result, shape.Antialiased);
    }
    public static DocumentSelection Invert(DocumentSelection selection, int width, int height)
    {
        using var p = Path(selection); using var canvas = CanvasPath(width, height); using var result = Operate(canvas, p, SKPathOp.Difference);
        return Snapshot(result, selection.Antialiased, selection.Feather);
    }
    public static DocumentSelection Move(DocumentSelection selection, double dx, double dy)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy) || Math.Abs(dx) > 1_000_000 || Math.Abs(dy) > 1_000_000)
            throw new ArgumentOutOfRangeException("Invalid selection movement.");
        using var path = Path(selection); using var builder = new SKPathBuilder { FillType = path.FillType };
        builder.AddPath(path, (float)dx, (float)dy);
        using var moved = builder.Detach(); return Snapshot(moved, selection.Antialiased, selection.Feather);
    }
    public static DocumentSelection Resize(DocumentSelection selection, double amount, int width, int height)
    {
        if (!double.IsFinite(amount) || Math.Abs(amount) > 500) throw new ArgumentOutOfRangeException(nameof(amount));
        selection.Validate();
        if (amount == 0 || selection.IsEmpty) return selection;
        using var path = Path(selection);
        using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)Math.Abs(amount * 2), StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var band = paint.GetFillPath(path);
        using var result = Operate(path, band, amount > 0 ? SKPathOp.Union : SKPathOp.Difference);
        using var canvas = CanvasPath(width, height); using var clipped = Operate(result, canvas, SKPathOp.Intersect);
        return Snapshot(clipped, selection.Antialiased, selection.Feather);
    }
    public static DocumentSelection Feather(DocumentSelection selection, double amount)
    {
        selection.Validate();
        if (!double.IsFinite(amount) || amount is < 0 or > 250) throw new ArgumentOutOfRangeException(nameof(amount));
        return selection with { Feather = Math.Min(250, Math.Sqrt(selection.Feather * selection.Feather + amount * amount)) };
    }
    public static bool Contains(DocumentSelection selection, PointD point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return false;
        using var path = Path(selection); return path.Contains((float)point.X, (float)point.Y);
    }
}
