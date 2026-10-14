using System.Windows;
using System.Windows.Input;

namespace Compositor.App;
public sealed partial class EditorCanvas
{
    private sealed record ZoomGesture(Point Start, double Zoom, double X, double Y, bool Moved = false);
    private ZoomGesture? zoomGesture;
    private void ApplyZoom(double value, Point anchor)
    {
        var before = DocumentPoint(anchor);Zoom = Math.Clamp(value, .001, 32);
        panX = anchor.X - before.X * Zoom;panY = anchor.Y - before.Y * Zoom;
        InvalidateVisual();ViewportChanged?.Invoke();
    }
    internal void SetZoomPercent(double percent)
    {
        if (!double.IsFinite(percent) || percent is < .1 or > 3200) throw new ArgumentOutOfRangeException(nameof(percent));
        if (HasInteraction || Session.InTransaction) return;
        ApplyZoom(percent / 100, new(ActualWidth / 2, ActualHeight / 2));
    }
    private void MoveZoom(Point point)
    {
        var drag = zoomGesture!;double dx = point.X - drag.Start.X;
        if (Math.Abs(dx) >= 3) zoomGesture = drag = drag with { Moved = true };
        if (drag.Moved) ApplyZoom(drag.Zoom * Math.Pow(2, dx / 100), drag.Start);
    }
    private void FinishZoom(ModifierKeys modifiers)
    {
        var drag = zoomGesture!;zoomGesture = null;gestureButton = null;
        if (!drag.Moved) ApplyZoom(Zoom * (modifiers.HasFlag(ModifierKeys.Alt) ? .5 : 2), drag.Start);
    }
    private void CancelZoom()
    {
        if (zoomGesture is not { } drag) return;
        zoomGesture = null;Zoom = drag.Zoom;panX = drag.X;panY = drag.Y;ViewportChanged?.Invoke();
    }
}
