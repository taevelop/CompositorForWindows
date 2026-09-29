using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Compositor.Core;

namespace Compositor.App;

public sealed class CurveGraph : FrameworkElement
{
    private ToneCurve curve = ToneCurve.Identity;
    private ToneCurve? dragOriginal;
    public ToneCurve Curve { get => curve; set { curve = value; SelectedIndex = Math.Clamp(SelectedIndex, 0, value.Points.Length - 1); InvalidateVisual(); } }
    public int SelectedIndex { get; private set; }
    public Func<bool>? FlushPending { get; set; }
    public event Action? Edited;
    private const double Pad = 12;
    public CurveGraph() { Focusable = true; Cursor = Cursors.Cross; }
    private Point Screen(double x, double y) => new(Pad + x / 255 * Math.Max(1, ActualWidth - 2 * Pad), Pad + (1 - y / 255) * Math.Max(1, ActualHeight - 2 * Pad));
    private CurvePoint Value(Point p) => new(Math.Clamp((p.X - Pad) / Math.Max(1, ActualWidth - 2 * Pad) * 255, 0, 255), Math.Clamp((1 - (p.Y - Pad) / Math.Max(1, ActualHeight - 2 * Pad)) * 255, 0, 255));
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(24, 26, 30)), null, new(0, 0, ActualWidth, ActualHeight));
        var grid = new Pen(new SolidColorBrush(Color.FromRgb(62, 66, 74)), 1);
        for (int i = 0; i <= 4; i++) { double n = i * 255.0 / 4; dc.DrawLine(grid, Screen(n, 0), Screen(n, 255)); dc.DrawLine(grid, Screen(0, n), Screen(255, n)); }
        dc.DrawLine(new Pen(Brushes.DimGray, 1), Screen(0, 0), Screen(255, 255));
        var geometry = new StreamGeometry();
        using (var path = geometry.Open()) { path.BeginFigure(Screen(0, curve.Value(0)), false, false); for (int x = 1; x <= 255; x++) path.LineTo(Screen(x, curve.Value(x)), true, false); }
        dc.DrawGeometry(null, new Pen(Brushes.DeepSkyBlue, 2), geometry);
        for (int i = 0; i < curve.Points.Length; i++) { var p = curve.Points[i]; dc.DrawEllipse(i == SelectedIndex ? Brushes.White : Brushes.DeepSkyBlue, new Pen(Brushes.Black, 1), Screen(p.X, p.Y), 5, 5); }
    }
    internal bool SelectPoint(int index)
    {
        if (FlushPending?.Invoke() == false) return false;
        SelectedIndex = Math.Clamp(index, 0, curve.Points.Length - 1); Edited?.Invoke(); InvalidateVisual(); return true;
    }
    internal bool BeginPoint(double x, double y, bool insert = false)
    {
        if (FlushPending?.Invoke() == false) return false;
        var position = Screen(x, y);
        int hit = -1; double distance = 12;
        for (int i = 0; !insert && i < curve.Points.Length; i++) { var p = curve.Points[i]; double d = (Screen(p.X, p.Y) - position).Length; if (d < distance) { hit = i; distance = d; } }
        dragOriginal = curve;
        if (hit >= 0) SelectedIndex = hit;
        else
        {
            if (curve.Points.Length == 32 || x <= 0 || x >= 255 || curve.Points.Any(p => p.X == x)) { dragOriginal = null; return false; }
            var points = curve.Points.Append(new CurvePoint(x, y)).OrderBy(p => p.X).ToArray(); SelectedIndex = Array.FindIndex(points, p => p.X == x); Curve = new(points);
        }
        Edited?.Invoke(); InvalidateVisual(); return true;
    }
    internal void MovePoint(double x, double y)
    {
        if (dragOriginal is null) return;
        var points = curve.Points.ToArray(); int i = SelectedIndex;
        if (i == 0 || i == points.Length - 1) x = points[i].X;
        else
        {
            // Use the Mac UI's one-unit gap, reduced for imported closely spaced points.
            double gap = Math.Min(1, (points[i + 1].X - points[i - 1].X) / 3);
            double low = Math.Max(Math.BitIncrement(points[i - 1].X), points[i - 1].X + gap);
            double high = Math.Min(Math.BitDecrement(points[i + 1].X), points[i + 1].X - gap);
            x = Math.Clamp(x, low, high);
        }
        points[i] = new(x, Math.Clamp(y, 0, 255)); Curve = new(points); Edited?.Invoke();
    }
    internal void EndPoint(bool commit)
    {
        var original = dragOriginal; dragOriginal = null;
        if (!commit && original is not null) { Curve = original; Edited?.Invoke(); }
        if (IsMouseCaptured) ReleaseMouseCapture();
    }
    internal bool DeletePoint()
    {
        if (FlushPending?.Invoke() == false || SelectedIndex == 0 || SelectedIndex == curve.Points.Length - 1) return false;
        Curve = new(curve.Points.RemoveAt(SelectedIndex).ToArray()); Edited?.Invoke(); return true;
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { Focus(); var p = Value(e.GetPosition(this)); if (BeginPoint(p.X, p.Y)) CaptureMouse(); e.Handled = true; }
    protected override void OnMouseMove(MouseEventArgs e) { if (IsMouseCaptured) { var p = Value(e.GetPosition(this)); MovePoint(p.X, p.Y); } }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { EndPoint(true); e.Handled = true; }
    protected override void OnLostMouseCapture(MouseEventArgs e) { EndPoint(false); base.OnLostMouseCapture(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && dragOriginal is not null) { EndPoint(false); e.Handled = true; }
        else if (e.Key == Key.Delete) { DeletePoint(); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
