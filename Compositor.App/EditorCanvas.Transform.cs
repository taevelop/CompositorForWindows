using System.Windows.Input;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
namespace Compositor.App;
public sealed partial class EditorCanvas
{
    private LayerTransformEdit? transformEdit;
    private Document? transformOriginal;
    private readonly ShapeTransformPreview shapeTransformPreview=new();
    internal Document DisplayDocument
    {
        get
        {
            if(transformEdit is not null&&transformOriginal is {} original)
                return shapeTransformPreview.Create(Session.Document,original);
            transformOriginal=null;shapeTransformPreview.Clear();return Session.Document;
        }
    }
    private TransformDrag? transformDrag;
    public bool IsTransforming=>transformEdit is not null;
    public LayerTransform? TransformDraft=>transformEdit?.Draft;
    public LayerTransform? TransformInitial=>transformEdit?.InitialTransform;
    public bool TransformLockRatio {get;set;}=true;
    public Action? TransformChanged {get;set;}
    public void BeginTransform()
    {
        if(IsTransforming)return;
        CancelInteraction();CancelCrop();transformOriginal=Session.Document;transformEdit=LayerTransformEdit.Begin(Session);
        Tool=EditorTool.Move;TransformChanged?.Invoke();InvalidateVisual();
    }
    public void PreviewTransform(LayerTransform value)
    {
        try{transformEdit?.Preview(value);TransformChanged?.Invoke();InvalidateVisual();}
        catch{transformEdit?.Dispose();transformEdit=null;transformOriginal=null;shapeTransformPreview.Clear();transformDrag=null;gestureButton=null;ReleaseGestureCapture();TransformChanged?.Invoke();throw;}
    }
    public void CommitTransform()
    {
        if(transformEdit is not {} edit)return;
        transformEdit=null;transformDrag=null;gestureButton=null;ReleaseGestureCapture();
        transformOriginal=null;shapeTransformPreview.Clear();
        Cursor=null;
        try { edit.Complete(); }
        finally { edit.Dispose();TransformChanged?.Invoke();InvalidateVisual(); }
    }
    public void CancelTransform()
    {
        var edit=transformEdit;transformEdit=null;transformDrag=null;gestureButton=null;ReleaseGestureCapture();
        transformOriginal=null;shapeTransformPreview.Clear();
        Cursor=null;edit?.Dispose();TransformChanged?.Invoke();InvalidateVisual();
    }
    private void CancelTransformDrag()
    {
        if(transformDrag is not {} drag)return;
        transformDrag=null;PreviewTransform(drag.Original);
    }
    private PointD RotationHandle(LayerTransform t)=>TransformDrag.Point(t,new(.5,-30/Zoom/t.Height));
    private bool BeginTransformPointer(PointD point)
    {
        if(transformEdit is not {} edit)return false;
        var t=edit.Draft;
        for(int i=0;i<8;i++)
        {
            var h=TransformDrag.Point(t,CropGeometry.Handles[i]);
            if(double.Hypot(point.X-h.X,point.Y-h.Y)*Zoom<=8)
            {transformDrag=new(t,point,TransformDragMode.Resize,i);return true;}
        }
        var rotation=RotationHandle(t);
        if(double.Hypot(point.X-rotation.X,point.Y-rotation.Y)*Zoom<=10)
        {transformDrag=new(t,point,TransformDragMode.Rotate);return true;}
        if(TransformDrag.Contains(t,point)){transformDrag=new(t,point,TransformDragMode.Move);return true;}
        return false;
    }
    private void MoveTransformPointer(PointD point,ModifierKeys keys)
    {
        if(transformDrag is {} drag)
            PreviewTransform(drag.Update(point,TransformLockRatio,keys.HasFlag(ModifierKeys.Shift),keys.HasFlag(ModifierKeys.Alt)));
    }
    internal void UpdateTransformCursor(PointD point)
    {
        if(TransformDraft is not {} t||HasInteraction)return;
        for(int i=0;i<8;i++)
        {
            var h=TransformDrag.Point(t,CropGeometry.Handles[i]);
            if(double.Hypot(point.X-h.X,point.Y-h.Y)*Zoom>8)continue;
            double angle=t.Rotation+((i%4) switch{0=>45,1=>90,2=>135,_=>0});
            int direction=((int)Math.Round(angle%180/45)+4)%4;
            Cursor=direction switch{0=>Cursors.SizeWE,1=>Cursors.SizeNWSE,2=>Cursors.SizeNS,_=>Cursors.SizeNESW};return;
        }
        var rotation=RotationHandle(t);
        Cursor=double.Hypot(point.X-rotation.X,point.Y-rotation.Y)*Zoom<=10?Cursors.Hand:
            TransformDrag.Contains(t,point)?Cursors.SizeAll:Cursors.Arrow;
    }
    private void DrawTransform(SKCanvas canvas)
    {
        if(TransformDraft is not {} t)return;
        using var line=new SKPaint{Color=new(108,154,224),Style=SKPaintStyle.Stroke,StrokeWidth=(float)(1/Zoom),IsAntialias=true};
        using var fill=new SKPaint{Color=SKColors.White};
        var points=CropGeometry.Handles.Select(h=>TransformDrag.Point(t,h)).ToArray();
        using var builder=new SKPathBuilder();
        builder.AddPoly(points.Where((_,i)=>i%2==0).Select(p=>new SKPoint((float)p.X,(float)p.Y)).ToArray(),true);
        using var path=builder.Detach();canvas.DrawPath(path,line);
        var rotation=RotationHandle(t);canvas.DrawLine((float)points[1].X,(float)points[1].Y,(float)rotation.X,(float)rotation.Y,line);
        canvas.DrawCircle((float)rotation.X,(float)rotation.Y,(float)(5/Zoom),fill);canvas.DrawCircle((float)rotation.X,(float)rotation.Y,(float)(5/Zoom),line);
        foreach(var p in points)
        {
            float radius=(float)(4/Zoom);var r=new SKRect((float)p.X-radius,(float)p.Y-radius,(float)p.X+radius,(float)p.Y+radius);
            canvas.DrawRect(r,fill);canvas.DrawRect(r,line);
        }
    }
}
