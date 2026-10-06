using System.Windows.Input;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;

namespace Compositor.App;
public sealed partial class EditorCanvas
{
    private CropFrame? cropDraft, cropBeforeDrag;
    private Document? cropDocument;
    private CropDrag? cropDrag;
    public string CropRatioChoice { get; private set; } = "Free";
    public bool CropSnapEnabled { get; set; } = true;
    public Action? CropChanged { get; set; }
    private double? CropRatio => CropRatioChoice switch
    { "Original" => (double)Session.Document.Width/Session.Document.Height, "1:1" => 1, "4:3" => 4d/3, "16:9" => 16d/9, _ => null };
    public CropFrame? CropDraft { get { ReconcileCrop(); return cropDraft; } }
    private CropFrame VisibleCrop => cropDraft ?? new(0,0,Session.Document.Width,Session.Document.Height);
    public void ReconcileCrop()
    {
        if (cropDocument is not null && !ReferenceEquals(cropDocument,Session.Document))
        { CancelCrop(); }
    }
    public void CancelCrop()
    {
        cropDrag=null;cropDraft=null;cropBeforeDrag=null;cropDocument=null;gestureButton=null;
        ReleaseGestureCapture();InvalidateVisual();CropChanged?.Invoke();
    }
    private void CancelCropDrag()
    {
        if(cropDrag is null)return;
        cropDraft=cropBeforeDrag;cropDrag=null;cropBeforeDrag=null;
        if(cropDraft is null)cropDocument=null;
        CropChanged?.Invoke();
    }
    public void ChangeCropRatio(string choice)
    {
        CancelInteraction();ReconcileCrop();CropRatioChoice=choice;
        if(CropRatio is {} ratio){cropDraft=CropGeometry.WithRatio(VisibleCrop,ratio);cropDocument=Session.Document;}
        InvalidateVisual();CropChanged?.Invoke();
    }
    public void CommitCrop()
    {
        ReconcileCrop();
        if(cropDraft is not {} frame || Session.InTransaction)return;
        cropDrag=null;gestureButton=null;ReleaseGestureCapture();
        Session.Apply(d=>CanvasCrop.Apply(d,frame));
        CancelCrop();Fit();
    }
    private void BeginCrop(PointD point)
    {
        ReconcileCrop();var frame=VisibleCrop;int handle=-1;
        for(int i=0;i<CropGeometry.Handles.Count;i++)
        {
            var h=CropGeometry.Handles[i];
            if(double.Hypot(point.X-(frame.X+h.X*frame.Width),point.Y-(frame.Y+h.Y*frame.Height))*Zoom<=8){handle=i;break;}
        }
        bool inside=cropDraft is not null && point.X>=frame.X&&point.X<=frame.Right&&point.Y>=frame.Y&&point.Y<=frame.Bottom;
        cropBeforeDrag=cropDraft;cropDocument=Session.Document;
        cropDrag=new(frame,point,handle>=0?CropDragMode.Resize:inside?CropDragMode.Move:CropDragMode.Create,Math.Max(0,handle));
    }
    private void MoveCrop(PointD point, ModifierKeys keys)
    {
        if(cropDrag is not {} drag)return;
        bool symmetric=keys.HasFlag(ModifierKeys.Alt);
        var next=drag.Update(point,CropRatio,symmetric);
        if(CropSnapEnabled)
        {
            var doc=Session.Document;
            List<double> xs=[0,doc.Width],ys=[0,doc.Height];
            foreach(var entry in LayerHierarchy.Entries(doc))
            {
                var layer=entry.Layer;if(!entry.Visible||layer.IsAdjustment||layer.IsGroup)continue;
                var t=layer.Transform;
                PointD[] corners=[new(0,0),new(layer.Pixels.Width,0),new(layer.Pixels.Width,layer.Pixels.Height),new(0,layer.Pixels.Height)];
                var points=corners.Select(p=>t.ToDocument(p,layer.Pixels.Width,layer.Pixels.Height)).ToArray();
                xs.Add(points.Min(p=>p.X));xs.Add(points.Max(p=>p.X));
                ys.Add(points.Min(p=>p.Y));ys.Add(points.Max(p=>p.Y));
            }
            next=CropSnapping.Apply(next,drag,point,CropRatio,symmetric,xs,ys,6/Zoom);
        }
        cropDraft=next;InvalidateVisual();CropChanged?.Invoke();
    }
    private void UpdateCropCursor(PointD point)
    {
        if(Tool!=EditorTool.Crop || HasInteraction)return;
        var f=VisibleCrop;
        for(int i=0;i<CropGeometry.Handles.Count;i++)
        {
            var h=CropGeometry.Handles[i];
            if(double.Hypot(point.X-f.X-h.X*f.Width,point.Y-f.Y-h.Y*f.Height)*Zoom<=8)
            {
                Cursor=i is 0 or 4?Cursors.SizeNWSE:i is 2 or 6?Cursors.SizeNESW:i is 1 or 5?Cursors.SizeNS:Cursors.SizeWE;
                return;
            }
        }
        Cursor=cropDraft is not null&&point.X>=f.X&&point.X<=f.Right&&point.Y>=f.Y&&point.Y<=f.Bottom?Cursors.SizeAll:Cursors.Cross;
    }
    private void DrawCrop(SKCanvas canvas)
    {
        if(Tool!=EditorTool.Crop)return;
        ReconcileCrop();var f=VisibleCrop;var rect=new SKRect(f.X,f.Y,(float)f.Right,(float)f.Bottom);
        canvas.Save();canvas.ClipRect(rect,SKClipOperation.Difference);
        using(var shade=new SKPaint{Color=new(0,0,0,145)})canvas.DrawRect(canvas.LocalClipBounds,shade);
        canvas.Restore();
        using var line=new SKPaint{Color=new(108,154,224),Style=SKPaintStyle.Stroke,StrokeWidth=(float)(1/Zoom),IsAntialias=true};
        canvas.DrawRect(rect,line);
        using var grid=new SKPaint{Color=new(255,255,255,100),StrokeWidth=(float)(1/Zoom)};
        for(int i=1;i<3;i++)
        {
            float x=f.X+f.Width*i/3f,y=f.Y+f.Height*i/3f;
            canvas.DrawLine(x,f.Y,x,(float)f.Bottom,grid);canvas.DrawLine(f.X,y,(float)f.Right,y,grid);
        }
        using var fill=new SKPaint{Color=SKColors.White};
        foreach(var h in CropGeometry.Handles)
        {
            float x=(float)(f.X+h.X*f.Width),y=(float)(f.Y+h.Y*f.Height),r=(float)(4/Zoom);
            var box=new SKRect(x-r,y-r,x+r,y+r);canvas.DrawRect(box,fill);canvas.DrawRect(box,line);
        }
    }
}
