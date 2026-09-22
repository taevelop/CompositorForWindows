using System.Windows;
using System.Windows.Input;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace Compositor.App;

public enum EditorTool { Move, Brush, Eraser, Hand }
public sealed class EditorCanvas : SKElement, IDisposable
{
    private readonly ViewportRenderer renderer = new();
    public EditorSession Session { get; set; } = null!;
    public EditorTool Tool { get; set; } = EditorTool.Brush;
    public Func<BrushSettings> ReadBrush { get; set; } = () => new(40, .7, 1, 98, 201, 181);
    public Action<string>? ReportError { get; set; }
    public double Zoom { get; private set; } = 1;
    private double panX = 30, panY = 30;
    private Point? panStart;
    private BrushStroke? stroke;
    private Layer? originalLayer;
    private PointD anchor;
    public EditorCanvas()
    {
        PaintSurface += Paint;
        Loaded += (_, _) => Fit();
        LostMouseCapture += (_, _) => { if (Session?.InTransaction == true) EndPointer(false); panStart = null; };
        Unloaded += (_, _) => Dispose();
    }
    public void Fit()
    {
        if (Session is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        Zoom = Math.Clamp(Math.Min((ActualWidth - 60) / Session.Document.Width, (ActualHeight - 60) / Session.Document.Height), .01, 32);
        panX = (ActualWidth - Session.Document.Width * Zoom) / 2; panY = (ActualHeight - Session.Document.Height * Zoom) / 2;
        InvalidateVisual();
    }
    public void ActualPixels() { Zoom = 1; panX = panY = 30; InvalidateVisual(); }
    private PointD DocumentPoint(Point p) => new((p.X - panX) / Zoom, (p.Y - panY) / Zoom);
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e); Point p = e.GetPosition(this); var before = DocumentPoint(p);
        Zoom = Math.Clamp(Zoom * Math.Pow(1.15, e.Delta / 120.0), .01, 32);
        panX = p.X - before.X * Zoom; panY = p.Y - before.Y * Zoom; InvalidateVisual(); e.Handled = true;
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e); Focus();
        if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && Tool == EditorTool.Hand))
        { panStart = e.GetPosition(this); CaptureMouse(); e.Handled = true; return; }
        if (e.ChangedButton != MouseButton.Left) return;
        try { BeginPointer(DocumentPoint(e.GetPosition(this))); if (Session.InTransaction) CaptureMouse(); }
        catch (Exception error) { EndPointer(false); ReportError?.Invoke(error.Message); }
        e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (panStart is Point previous)
        { var p = e.GetPosition(this); panX += p.X - previous.X; panY += p.Y - previous.Y; panStart = p; InvalidateVisual(); return; }
        if (!Session.InTransaction) return;
        try { MovePointer(DocumentPoint(e.GetPosition(this))); }
        catch (Exception error) { EndPointer(false); ReleaseMouseCapture(); ReportError?.Invoke(error.Message); }
    }
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        try { if (Session.InTransaction) { MovePointer(DocumentPoint(e.GetPosition(this))); EndPointer(true); } }
        catch (Exception error) { EndPointer(false); ReportError?.Invoke(error.Message); }
        panStart = null; ReleaseMouseCapture();
    }
    public void BeginPointer(PointD point)
    {
        if (Session.InTransaction || Session.ActiveLayer is not { } layer) return;
        if (Tool is EditorTool.Brush or EditorTool.Eraser && !layer.Visible) throw new InvalidOperationException("Show the layer before painting.");
        originalLayer = layer; anchor = point;
        if (Tool is EditorTool.Brush or EditorTool.Eraser)
            stroke = new(layer, ReadBrush() with { Erase = Tool == EditorTool.Eraser }, Session.Document.Width, Session.Document.Height);
        Session.Begin(); MovePointer(point);
    }
    public void MovePointer(PointD point)
    {
        if (!Session.InTransaction || originalLayer is null) return;
        if (stroke is not null)
        { stroke.Append(point); if (!ReferenceEquals(Session.ActiveLayer?.Pixels, stroke.Pixels)) Session.Preview(Session.Document.Replace(originalLayer with { Pixels = stroke.Pixels })); }
        else if (Tool == EditorTool.Move)
        {
            var t = originalLayer.Transform;
            Session.Preview(Session.Document.Replace(originalLayer with { Transform = t with { X = t.X + point.X - anchor.X, Y = t.Y + point.Y - anchor.Y } }));
        }
    }
    public void EndPointer(bool commit)
    {
        stroke = null; originalLayer = null;
        if (commit) Session.Commit(); else Session.Cancel();
        InvalidateVisual();
    }
    private void Paint(object? sender, SKPaintSurfaceEventArgs e)
    {
        var c = e.Surface.Canvas; c.Clear(new SKColor(28, 30, 34));
        if (Session is null || ActualWidth <= 0) return;
        c.Save(); c.Scale(e.Info.Width / (float)ActualWidth, e.Info.Height / (float)ActualHeight);
        c.Translate((float)panX, (float)panY); c.Scale((float)Zoom);
        var doc = Session.Document;
        using (var background = new SKPaint { Color = new(224, 227, 232) }) c.DrawRect(0, 0, doc.Width, doc.Height, background);
        // Keep checker cells constant in screen space and only draw the visible area.
        double cell = 12 / Zoom;
        var visible = c.LocalClipBounds;
        using (var checker = new SKPaint { Color = new(197, 201, 209) })
        {
            c.Save(); c.ClipRect(new(0, 0, doc.Width, doc.Height));
            for (int y = (int)Math.Max(0, Math.Floor(visible.Top / cell)); y < Math.Min(doc.Height, visible.Bottom) / cell; y++)
            for (int x = (int)Math.Max(0, Math.Floor(visible.Left / cell)); x < Math.Min(doc.Width, visible.Right) / cell; x++)
                if ((x + y) % 2 == 0) c.DrawRect((float)(x * cell), (float)(y * cell), (float)cell, (float)cell, checker);
            c.Restore();
        }
        using (var frame = renderer.Render(doc, e.Info.Width, e.Info.Height, (float)(Zoom * e.Info.Width / ActualWidth),
            (float)(panX * e.Info.Width / ActualWidth), (float)(panY * e.Info.Height / ActualHeight)))
        { c.Save(); c.ResetMatrix(); c.DrawImage(frame, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest)); c.Restore(); }
        if (Session.ActiveLayer is { } layer && Tool == EditorTool.Move)
        {
            using var outline = new SKPaint { Color = new(98, 201, 181), Style = SKPaintStyle.Stroke, StrokeWidth = (float)(1 / Zoom), IsAntialias = true };
            using var path = new SKPathBuilder();
            PointD[] corners = [new(0, 0), new(layer.Pixels.Width, 0), new(layer.Pixels.Width, layer.Pixels.Height), new(0, layer.Pixels.Height)];
            for (int i = 0; i < 4; i++) { var p = layer.Transform.ToDocument(corners[i], layer.Pixels.Width, layer.Pixels.Height); if (i == 0) path.MoveTo((float)p.X, (float)p.Y); else path.LineTo((float)p.X, (float)p.Y); }
            path.Close(); using var outlinePath = path.Detach(); c.DrawPath(outlinePath, outline);
        }
        c.Restore();
    }
    public void Dispose() => renderer.Dispose();
}
