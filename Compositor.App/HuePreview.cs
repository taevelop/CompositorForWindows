using System.Windows;
using System.Windows.Input;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace Compositor.App;

internal sealed class HuePreview : SKElement, IDisposable
{
    private readonly ViewportRenderer renderer = new();
    private Document? sampledDocument;
    private SKBitmap? sampled;
    public required Func<Document> Document { get; init; }
    public Func<PointD, bool>? BeginSample { get; set; }
    public Action<double, bool>? DragSample { get; set; }
    public Action<bool>? EndSample { get; set; }
    public Action<string>? Error { get; set; }
    private double zoom = 1, panX = 20, panY = 20;
    private Point? start, previous;
    private Point originalPan;
    private MouseButton? button;
    public HuePreview()
    {
        Focusable = true; Cursor = Cursors.Cross;
        PaintSurface += Paint; Loaded += (_, _) => Fit();
        LostMouseCapture += (_, _) => CancelGesture();
    }
    public void Fit()
    {
        CancelGesture(); var d = Document();
        zoom = Math.Clamp(Math.Min((ActualWidth - 40) / d.Width, (ActualHeight - 40) / d.Height), .01, 32);
        panX = (ActualWidth - d.Width * zoom) / 2; panY = (ActualHeight - d.Height * zoom) / 2; InvalidateVisual();
    }
    internal PointD DocumentPoint(Point p) => new((p.X-panX)/zoom,(p.Y-panY)/zoom);
    internal Point ViewPoint(PointD p) => new(p.X*zoom+panX,p.Y*zoom+panY);
    public double? SampleHue(PointD point)
    {
        var d = Document();
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || point.X < 0 || point.Y < 0 || point.X >= d.Width || point.Y >= d.Height) return null;
        if (!ReferenceEquals(d,sampledDocument))
        {
            sampled?.Dispose(); sampled=null; sampledDocument=null;
            using var r=new CanvasRenderer();using var image=r.Flatten(d);
            var bitmap=new SKBitmap(CanvasRenderer.Info(d.Width,d.Height));
            if(!image.ReadPixels(bitmap.Info,bitmap.GetPixels(),bitmap.RowBytes,0,0)){bitmap.Dispose();throw new InvalidOperationException("Cannot sample image.");}
            sampled=bitmap;sampledDocument=d;
        }
        int p=((int)point.Y*d.Width+(int)point.X)*4;
        var rgba=sampled!.GetPixelSpan();double a=rgba[p+3];if(a==0)return null;
        double r0=rgba[p]/a,g=rgba[p+1]/a,b=rgba[p+2]/a;
        double high=Math.Max(r0,Math.Max(g,b)),low=Math.Min(r0,Math.Min(g,b));
        if(high==0||(high-low)/high<=.02)return null;
        return HueSaturationProcessor.ToHsl(r0,g,b).Hue;
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);Focus();
        try { if(!BeginInteraction(e.ChangedButton,e.GetPosition(this)))return;if(!CaptureMouse())CancelGesture();e.Handled=true; }
        catch(Exception ex){CancelGesture();Error?.Invoke(ex.Message);}
    }
    internal bool BeginInteraction(MouseButton changed,Point point)
    {
        if(button is not null)return false;
        if(changed==MouseButton.Middle){originalPan=new(panX,panY);previous=point;}
        else if(changed!=MouseButton.Left||BeginSample?.Invoke(DocumentPoint(point))!=true)return false;
        start=point;button=changed;return true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        try {MoveInteraction(e.GetPosition(this),(Keyboard.Modifiers&ModifierKeys.Control)!=0);}
        catch(Exception ex){CancelGesture();Error?.Invoke(ex.Message);}
    }
    internal void MoveInteraction(Point p,bool control)
    {
        if(button==MouseButton.Middle&&previous is Point old){panX+=p.X-old.X;panY+=p.Y-old.Y;previous=p;InvalidateVisual();}
        else if(button==MouseButton.Left&&start is Point origin)DragSample?.Invoke(p.X-origin.X,control);
    }
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        try {if(FinishInteraction(e.ChangedButton,e.GetPosition(this),(Keyboard.Modifiers&ModifierKeys.Control)!=0))e.Handled=true;}
        catch(Exception ex){CancelGesture();Error?.Invoke(ex.Message);}
    }
    internal bool FinishInteraction(MouseButton changed,Point point,bool control)
    {
        if(button!=changed)return false;
        MoveInteraction(point,control);bool edit=button==MouseButton.Left;
        button=null;start=previous=null;if(edit)EndSample?.Invoke(true);if(IsMouseCaptured)ReleaseMouseCapture();return true;
    }
    public void CancelGesture()
    {
        bool edit=button==MouseButton.Left;
        if(button==MouseButton.Middle){panX=originalPan.X;panY=originalPan.Y;}
        button=null;start=previous=null;if(edit)EndSample?.Invoke(false);
        if(IsMouseCaptured)ReleaseMouseCapture();InvalidateVisual();
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);e.Handled=true;if(button is not null)return;
        var p=e.GetPosition(this);var before=DocumentPoint(p);
        zoom=Math.Clamp(zoom*Math.Pow(1.15,e.Delta/120d),.01,32);
        panX=p.X-before.X*zoom;panY=p.Y-before.Y*zoom;InvalidateVisual();
    }
    private void Paint(object? sender,SKPaintSurfaceEventArgs e)
    {
        var c=e.Surface.Canvas;c.Clear(new SKColor(28,30,34));if(ActualWidth<=0||ActualHeight<=0)return;
        try
        {
            var d=Document();float scale=e.Info.Width/(float)ActualWidth;
            c.Save();c.Translate((float)panX*scale,(float)panY*scale);c.Scale((float)zoom*scale);
            using var paper=new SKPaint{Color=new(224,227,232)};c.DrawRect(0,0,d.Width,d.Height,paper);
            c.Restore();
            using var image=renderer.Render(d,e.Info.Width,e.Info.Height,(float)zoom*scale,(float)panX*scale,(float)panY*scale);
            c.DrawImage(image,0,0,new SKSamplingOptions(SKFilterMode.Nearest));
        }
        catch(Exception ex){Error?.Invoke(ex.Message);}
    }
    public void Dispose(){sampled?.Dispose();sampled=null;sampledDocument=null;renderer.Dispose();}
}
