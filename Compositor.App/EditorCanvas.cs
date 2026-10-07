using System.Windows;
using System.Windows.Input;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace Compositor.App;

public enum EditorTool { Move, Brush, Eraser, Hand, RectangleSelection, EllipseSelection, FreehandSelection, PolygonSelection, Crop, Eyedropper }
public sealed partial class EditorCanvas : SKElement, IDisposable
{
    private readonly ViewportRenderer renderer = new();
    public EditorSession Session { get; set; } = null!;
    public EditorTool Tool { get; set; } = EditorTool.Brush;
    public Func<BrushSettings> ReadBrush { get; set; } = () => new(40, .7, 1, 32, 32, 32);
    public Action<string>? ReportError { get; set; }
    public Action? ViewportChanged { get; set; }
    public double Zoom { get; private set; } = 1;
    public bool HasInteraction => transformDrag is not null || cropDrag is not null || pixelMove is not null || selectionStart is not null || originalLayer is not null || panStart is not null || gestureButton is not null;
    private double panX = 30, panY = 30;
    private Point? panStart;
    private Point panOrigin;
    private MouseButton? gestureButton;
    private BrushStroke? stroke;
    private SelectionPixelMoveEdit? pixelMove;
    private MaskStroke? maskStroke;
    private Layer? originalLayer;
    private Document? groupMoveDocument;
    private GroupTransform? multipleMove;
    private Document? selectionStart;
    private SelectionMode gestureSelectionMode;
    private bool movingSelection, constrainArmed;
    private DocumentSelection? drawnSelection;
    private readonly List<PointD> lassoPoints = [];
    private PointD? polygonCursor;
    private bool releasingGestureCapture;
    private bool IsLassoTool => Tool is EditorTool.FreehandSelection or EditorTool.PolygonSelection;
    internal bool PolygonActive => Tool == EditorTool.PolygonSelection && selectionStart is not null && !movingSelection;
    public bool SelectionFromCenter { get; set; }
    public bool IsSelectionTool => Tool is EditorTool.RectangleSelection or EditorTool.EllipseSelection or EditorTool.FreehandSelection or EditorTool.PolygonSelection;
    private static double Whole(double n) => Math.Round(n, MidpointRounding.AwayFromZero);
    public SelectionMode SelectionMode { get; set; }
    public bool SelectionAntialiased { get; set; } = true;
    private PointD anchor;
    public EditorCanvas()
    {
        InitializeSelectionAutoScroll();
        PaintSurface += Paint;
        Loaded += (_, _) => Fit();
        LostMouseCapture += (_, _) => { if (!releasingGestureCapture) CancelInteraction(); };
        Unloaded += (_, _) => { CancelInteraction(); Dispose(); };
    }
    internal (double Zoom,double X,double Y) CaptureView() => (Zoom,panX,panY);
    internal void RestoreView(double zoom,double x,double y)
    {
        CancelInteraction();Zoom=zoom;panX=x;panY=y;InvalidateVisual();ViewportChanged?.Invoke();
    }
    public void Fit()
    {
        if (Session is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        CancelInteraction();
        Zoom = Math.Clamp(Math.Min((ActualWidth - 60) / Session.Document.Width, (ActualHeight - 60) / Session.Document.Height), .01, 32);
        panX = (ActualWidth - Session.Document.Width * Zoom) / 2; panY = (ActualHeight - Session.Document.Height * Zoom) / 2;
        InvalidateVisual(); ViewportChanged?.Invoke();
    }
    public void ActualPixels() { CancelInteraction(); Zoom = 1; panX = panY = 30; InvalidateVisual(); ViewportChanged?.Invoke(); }
    internal PointD DocumentPoint(Point p) => new((p.X - panX) / Zoom, (p.Y - panY) / Zoom);
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e); e.Handled = true;
        ZoomAt(e.GetPosition(this), e.Delta);
    }
    internal void ZoomAt(Point point, int delta)
    {
        if (HasInteraction) return;
        var before = DocumentPoint(point);
        Zoom = Math.Clamp(Zoom * Math.Pow(1.15, delta / 120.0), .01, 32);
        panX = point.X - before.X * Zoom; panY = point.Y - before.Y * Zoom;
        InvalidateVisual(); ViewportChanged?.Invoke();
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e); Focus();
        try
        {
            if (!BeginInteraction(e.ChangedButton, e.GetPosition(this), e.ClickCount)) return;
            if (HasInteraction && !CaptureMouse()) CancelInteraction();
            e.Handled = true;
        }
        catch (Exception error) { CancelInteraction(); ReportError?.Invoke(error.Message); }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        try { MoveInteraction(e.GetPosition(this)); UpdateCropCursor(DocumentPoint(e.GetPosition(this))); UpdateTransformCursor(DocumentPoint(e.GetPosition(this))); }
        catch (Exception error) { CancelInteraction(); ReportError?.Invoke(error.Message); }
    }
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        try
        {
            if (!FinishInteraction(e.ChangedButton, e.GetPosition(this))) return;
            ReleaseGestureCapture(); e.Handled = true;
        }
        catch (Exception error) { CancelInteraction(); ReportError?.Invoke(error.Message); }
    }
    // These routes are also exercised by the hidden WPF integration check.
    internal bool BeginInteraction(MouseButton button, Point point, int clickCount = 1, ModifierKeys? modifiers = null)
    {
        if (IsTransforming)
        {
            if (!HasInteraction && button == MouseButton.Middle) { gestureButton = button; panOrigin = new(panX, panY); panStart = point; return true; }
            if (HasInteraction || button != MouseButton.Left || !BeginTransformPointer(DocumentPoint(point))) return false;
            gestureButton = button; return true;
        }
        if (PolygonActive)
        {
            if (button != MouseButton.Left || gestureButton is not null) return false;
            var p = DocumentPoint(point);
            if (clickCount >= 2 || (lassoPoints.Count >= 3 && double.Hypot(p.X - lassoPoints[0].X, p.Y - lassoPoints[0].Y) * Zoom <= 8))
            { EndPointer(true); ReleaseGestureCapture(); return true; }
            AppendLassoPoint(p); polygonCursor = p; gestureButton = button; InvalidateVisual(); return true;
        }
        if (Session is null || HasInteraction || Session.InTransaction) return false;
        if (button == MouseButton.Middle || (button == MouseButton.Left && Tool == EditorTool.Hand))
        {
            gestureButton = button; panOrigin = new(panX, panY); panStart = point; return true;
        }
        if (button != MouseButton.Left) return false;
        var keys=modifiers??Keyboard.Modifiers;
        if(Tool==EditorTool.Eyedropper||(keys.HasFlag(ModifierKeys.Alt)&&Tool is EditorTool.Brush or EditorTool.Eraser))
        {BeginColorSampling(button,point);return true;}
        BeginPointer(DocumentPoint(point), modifiers);
        if (!Session.InTransaction && cropDrag is null) return false;
        gestureButton = button; return true;
    }
    internal void MoveInteraction(Point point)
    {
        if(samplingColor){SampleAt(point);return;}
        if (panStart is Point previous)
        {
            panX += point.X - previous.X; panY += point.Y - previous.Y; panStart = point; InvalidateVisual(); return;
        }
        if (transformDrag is not null || cropDrag is not null || pixelMove is not null || originalLayer is not null || selectionStart is not null) MovePointer(DocumentPoint(point));
        UpdateSelectionAutoScroll(point);
    }
    internal bool FinishInteraction(MouseButton button, Point point)
    {
        if (gestureButton != button) return false;
        MoveInteraction(point);
        if(samplingColor){EndColorSampling();return true;}
        if (panStart is not null) { panStart = null; gestureButton = null; }
        else if (PolygonActive) gestureButton = null;
        else EndPointer(true);
        return true;
    }
    internal void ReleaseGestureCapture()
    {
        StopSelectionAutoScroll();
        releasingGestureCapture = true;
        try { if (IsMouseCaptured) ReleaseMouseCapture(); }
        finally { releasingGestureCapture = false; }
    }
    private void AppendLassoPoint(PointD point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) throw new ArgumentException("Invalid lasso point.");
        if (lassoPoints.Count > 0 && double.Hypot(point.X - lassoPoints[^1].X, point.Y - lassoPoints[^1].Y) < .25) return;
        if (lassoPoints.Count >= 100_000) throw new InvalidOperationException("The selection outline is too complex.");
        lassoPoints.Add(point); drawnSelection = SelectionGeometry.Polygon(lassoPoints, SelectionAntialiased);
    }
    internal bool HandleSelectionKey(Key key)
    {
        if (!PolygonActive || key is not (Key.Enter or Key.Back or Key.Delete or Key.Escape)) return false;
        try
        {
            if (key == Key.Escape) CancelInteraction();
            else if (key == Key.Enter) EndPointer(true);
            else
            {
                lassoPoints.RemoveAt(lassoPoints.Count - 1);
                if (lassoPoints.Count == 0) CancelInteraction();
                else { drawnSelection = SelectionGeometry.Polygon(lassoPoints, SelectionAntialiased); InvalidateVisual(); }
            }
            if (PolygonActive) gestureButton = null;
            ReleaseGestureCapture(); return true;
        }
        catch (Exception error) { CancelInteraction(); ReportError?.Invoke(error.Message); return true; }
    }
    public void CancelInteraction()
    {
        StopSelectionAutoScroll();
        if(samplingColor)EndColorSampling();
        CancelTransformDrag();
        CancelCropDrag();
        bool editing = originalLayer is not null || selectionStart is not null;
        pixelMove?.Dispose(); pixelMove = null;
        selectionStart = null; movingSelection = false; drawnSelection = null; lassoPoints.Clear(); polygonCursor = null;
        originalLayer = null; groupMoveDocument = null; multipleMove = null; stroke = null; maskStroke = null; gestureButton = null;
        if (panStart is not null) { panX = panOrigin.X; panY = panOrigin.Y; panStart = null; }
        if (editing && Session?.InTransaction == true) Session.Cancel();
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
    }
    public void BeginPointer(PointD point, ModifierKeys? modifiers = null)
    {
        if (Session.InTransaction) return;
        if(Tool==EditorTool.Eyedropper){SampleDocumentColor(point);return;}
        var keys = modifiers ?? Keyboard.Modifiers;
        if (Tool == EditorTool.Crop) { BeginCrop(point); return; }
        if (IsSelectionTool && keys.HasFlag(ModifierKeys.Control) && Session.Document.Selection is { IsEmpty: false } selectedPixels && SelectionGeometry.Contains(selectedPixels, point))
        {
            pixelMove = SelectionPixelMoveEdit.Begin(Session, keys.HasFlag(ModifierKeys.Alt));
            anchor = point; return;
        }
        if (IsSelectionTool)
        {
            selectionStart = Session.Document;
            constrainArmed = !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            gestureSelectionMode = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? SelectionMode.Subtract : Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? SelectionMode.Add : SelectionMode;
            movingSelection = gestureSelectionMode == SelectionMode.Replace && selectionStart.Selection is { IsEmpty: false } selected && SelectionGeometry.Contains(selected, point);
            anchor = movingSelection || IsLassoTool ? point : new(Whole(point.X), Whole(point.Y));
            lassoPoints.Clear(); drawnSelection = null; polygonCursor = null;
            if (Tool == EditorTool.PolygonSelection && !movingSelection) AppendLassoPoint(point);
            Session.Begin(); MovePointer(point); return;
        }
        if (Session.ActiveLayer is not { } layer || Tool == EditorTool.Hand) return;
        if (Tool is EditorTool.Brush or EditorTool.Eraser)
        {
            if (layer.IsAdjustment && !Session.EditMask) throw new InvalidOperationException("Adjustment layers have no image pixels. Select Edit mask to paint coverage.");
            if (layer.IsGroup) throw new InvalidOperationException("Select an image layer inside the group before painting.");
            if (!LayerHierarchy.Entries(Session.Document).First(e => e.Layer.Id == layer.Id).Visible) throw new InvalidOperationException("Show the layer and its parent groups before painting.");
        }
        multipleMove = Tool == EditorTool.Move && Session.SelectedLayerIds.Count > 1 ? new GroupTransform(Session.Document, Session.SelectedLayerIds) : null;
        originalLayer = layer; anchor = point;
        groupMoveDocument = layer.IsGroup ? Session.Document : null;
        if (Tool is EditorTool.Brush or EditorTool.Eraser)
        {
            var settings = ReadBrush() with { Erase = Tool == EditorTool.Eraser };
            var selection = Session.Document.Selection is { } selected ? SelectionCoverage.Create(selected, Session.Document.Width, Session.Document.Height) : null;
            if (Session.EditMask) maskStroke = new(layer, settings, Session.Document.Width, Session.Document.Height, selection);
            else stroke = new(layer, settings, Session.Document.Width, Session.Document.Height, selection);
        }
        Session.Begin(); MovePointer(point);
    }
    public void MovePointer(PointD point, ModifierKeys? modifiers = null)
    {
        if (transformDrag is not null) { MoveTransformPointer(point, modifiers ?? Keyboard.Modifiers); return; }
        if (cropDrag is not null) { MoveCrop(point, modifiers ?? Keyboard.Modifiers); return; }
        if (!Session.InTransaction) return;
        if (pixelMove is not null)
        {
            double dx = point.X - anchor.X, dy = point.Y - anchor.Y;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0; else dx = 0; }
            pixelMove.Preview(dx, dy); return;
        }
        if (selectionStart is { } start)
        {
            if (movingSelection)
            {
                double dx = Whole(point.X - anchor.X), dy = Whole(point.Y - anchor.Y);
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0; else dx = 0; }
                Session.Preview(dx == 0 && dy == 0 ? start : start with { Selection = SelectionGeometry.Move(start.Selection!, dx, dy) });
                return;
            }
            if (PolygonActive) { polygonCursor = point; InvalidateVisual(); return; }
            if (Tool == EditorTool.FreehandSelection) AppendLassoPoint(point);
            else
            {
                double width = Whole(point.X) - anchor.X, height = Whole(point.Y) - anchor.Y;
                if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) constrainArmed = true;
                if (constrainArmed && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                { double size = Math.Max(Math.Abs(width), Math.Abs(height)); width = Math.CopySign(size, width); height = Math.CopySign(size, height); }
                drawnSelection = SelectionGeometry.Box(SelectionFromCenter ? anchor.X - width : anchor.X, SelectionFromCenter ? anchor.Y - height : anchor.Y,
                    SelectionFromCenter ? width * 2 : width, SelectionFromCenter ? height * 2 : height, Tool == EditorTool.EllipseSelection, SelectionAntialiased);
            }
            if (Tool == EditorTool.FreehandSelection) { InvalidateVisual(); return; }
            Session.Preview(drawnSelection is null || drawnSelection.IsEmpty ? start : start with { Selection = SelectionGeometry.Combine(start.Selection, drawnSelection, gestureSelectionMode, start.Width, start.Height) });
            return;
        }
        if (originalLayer is null) return;
        if (maskStroke is not null)
        {
            maskStroke.Append(point);
            if (!ReferenceEquals(Session.ActiveLayer?.Mask, maskStroke.Mask)) Session.Preview(Session.Document.Replace(originalLayer with { Mask = maskStroke.Mask }));
        }
        else if (stroke is not null)
        { stroke.Append(point); if (!ReferenceEquals(Session.ActiveLayer?.Pixels, stroke.Pixels)) Session.Preview(Session.Document.Replace(originalLayer with { Pixels = stroke.Pixels })); }
        else if (Tool == EditorTool.Move)
        {
            if (multipleMove is {} moving)
            {
                Session.Preview(moving.Apply(moving.Bounds with { X = moving.Bounds.X + point.X - anchor.X, Y = moving.Bounds.Y + point.Y - anchor.Y }));
                return;
            }
            if (groupMoveDocument is not null)
            {
                Session.Preview(LayerHierarchy.Translate(groupMoveDocument, originalLayer.Id, point.X - anchor.X, point.Y - anchor.Y));
                return;
            }
            var t = originalLayer.Transform;
            var transform = t with { X = t.X + point.X - anchor.X, Y = t.Y + point.Y - anchor.Y };
            if (Session.ActiveLayer?.Transform != transform) Session.Preview(Session.Document.Replace(originalLayer with { Transform = transform }));
        }
    }
    public void EndPointer(bool commit)
    {
        StopSelectionAutoScroll();
        if (transformDrag is not null)
        {
            if (!commit) CancelTransformDrag();
            transformDrag = null; gestureButton = null; InvalidateVisual(); return;
        }
        if (cropDrag is not null)
        {
            if (!commit) CancelCropDrag();
            cropDrag = null; cropBeforeDrag = null; gestureButton = null;
            InvalidateVisual(); CropChanged?.Invoke(); return;
        }
        if (pixelMove is { } move)
        {
            pixelMove = null; gestureButton = null;
            try { if (commit) move.Complete(); } finally { move.Dispose(); InvalidateVisual(); }
            return;
        }
        if (commit && selectionStart is { } lassoStart && !movingSelection && IsLassoTool && drawnSelection is { IsEmpty: false } shape)
            Session.Preview(lassoStart with { Selection = SelectionGeometry.Combine(lassoStart.Selection, shape, gestureSelectionMode, lassoStart.Width, lassoStart.Height) });
        if (commit && selectionStart is { } start && gestureSelectionMode == SelectionMode.Replace &&
            (movingSelection ? ReferenceEquals(start, Session.Document) : drawnSelection?.IsEmpty != false))
            Session.Preview(start with { Selection = null });
        bool editing = originalLayer is not null || selectionStart is not null;
        pixelMove?.Dispose(); pixelMove = null;
        selectionStart = null; movingSelection = false; drawnSelection = null; lassoPoints.Clear(); polygonCursor = null;
        stroke = null; maskStroke = null; originalLayer = null; groupMoveDocument = null; multipleMove = null; gestureButton = null;
        if (editing) { if (commit) Session.Commit(); else Session.Cancel(); }
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
        if (Session.ActiveLayer is { } layer && Tool == EditorTool.Move && !IsTransforming)
        {
            using var outline = new SKPaint { Color = new(108, 154, 224), Style = SKPaintStyle.Stroke, StrokeWidth = (float)(1 / Zoom), IsAntialias = true };
            using var path = new SKPathBuilder();
            var outlineTransform = layer.IsGroup ? LayerHierarchy.Bounds(doc, layer.Id) : layer.Transform;
            if (Session.SelectedLayerIds.Count > 1)
            {
                try { outlineTransform = new GroupTransform(doc, Session.SelectedLayerIds).Bounds; }
                catch (InvalidOperationException) { } // No visible images; Ctrl+T explains why transformation is unavailable.
                catch (System.IO.InvalidDataException) { } // A combined box can exceed the transform limit.
            }
            PointD[] corners = [new(0, 0), new(layer.Pixels.Width, 0), new(layer.Pixels.Width, layer.Pixels.Height), new(0, layer.Pixels.Height)];
            for (int i = 0; i < 4; i++) { var p = outlineTransform.ToDocument(corners[i], layer.Pixels.Width, layer.Pixels.Height); if (i == 0) path.MoveTo((float)p.X, (float)p.Y); else path.LineTo((float)p.X, (float)p.Y); }
            path.Close(); using var outlinePath = path.Detach(); c.DrawPath(outlinePath, outline);
        }
        if (doc.Selection is { IsEmpty: false } selected)
        {
            using var path = SelectionGeometry.Path(selected);
            using var white = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = (float)(1 / Zoom), IsAntialias = true };
            using var dash = SKPathEffect.CreateDash([(float)(4 / Zoom), (float)(4 / Zoom)], 0);
            using var black = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = (float)(1 / Zoom), IsAntialias = true, PathEffect = dash };
            c.Save(); c.ClipRect(new(0, 0, doc.Width, doc.Height)); c.DrawPath(path, white); c.DrawPath(path, black); c.Restore();
        }
        if (selectionStart is not null && !movingSelection && IsLassoTool && lassoPoints.Count > 0)
        {
            using var builder = new SKPathBuilder(); builder.AddPoly(lassoPoints.Select(p => new SKPoint((float)p.X, (float)p.Y)).ToArray(), false);
            if (PolygonActive && polygonCursor is { } cursor) builder.LineTo((float)cursor.X, (float)cursor.Y);
            using var path = builder.Detach();
            using var under = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = (float)(2 / Zoom), IsAntialias = true };
            using var line = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = (float)(1 / Zoom), IsAntialias = true };
            c.DrawPath(path, under); c.DrawPath(path, line);
        }
        DrawCrop(c);
        DrawTransform(c);
        c.Restore();
        c.Save();c.Scale(e.Info.Width/(float)ActualWidth,e.Info.Height/(float)ActualHeight);DrawSampleRing(c);c.Restore();
    }
    public void Dispose() { CancelTransform(); StopSelectionAutoScroll(); renderer.Dispose(); }
}
