using System.Windows.Input;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
namespace Compositor.App;
public sealed partial class EditorCanvas
{
    private ShapeDrag? shapeDrag;
    private ShapeDraft? shapeDraft;
    public Func<LayerShapeStyle> ReadShape {get;set;}=()=>new(ShapeKind.Rectangle,0,0,0);
    internal bool IsDrawingShape=>shapeDrag is not null;
    private void BeginShape(PointD point,ModifierKeys keys)
    {
        shapeDrag=new(point,ReadShape());Session.Begin();MoveShape(point,keys);
    }
    private void MoveShape(PointD point,ModifierKeys keys)
    {shapeDraft=shapeDrag!.Update(point,keys.HasFlag(ModifierKeys.Shift),keys.HasFlag(ModifierKeys.Alt));InvalidateVisual();}
    private void EndShape(bool commit)
    {
        var draft=shapeDraft;shapeDraft=null;shapeDrag=null;Session.Cancel();
        try{if(commit&&draft is not null)ShapeInsert.Add(Session,draft);}
        finally{gestureButton=null;InvalidateVisual();}
    }
    private void DrawShape(SKCanvas canvas)
    {
        if(shapeDraft is not {} draft)return;
        var t=draft.Transform;var style=draft.Style;
        using var paint=new SKPaint{IsAntialias=true,Color=new SKColor((byte)Math.Round(style.Red*255),(byte)Math.Round(style.Green*255),(byte)Math.Round(style.Blue*255),180)};
        var rect=new SKRect((float)t.X,(float)t.Y,(float)(t.X+t.Width),(float)(t.Y+t.Height));
        if(style.Kind==ShapeKind.Line)
        {
            paint.Style=SKPaintStyle.Stroke;paint.StrokeCap=SKStrokeCap.Round;paint.StrokeWidth=(float)Math.Max(1,style.LineWidth??1);
            var start=t.ToDocument(style.Start!.Value,1,1);var end=t.ToDocument(style.End!.Value,1,1);
            canvas.DrawLine((float)start.X,(float)start.Y,(float)end.X,(float)end.Y,paint);
        }
        else if(style.Kind==ShapeKind.Ellipse)canvas.DrawOval(rect,paint);
        else{float radius=(float)Math.Min(style.CornerRadius,Math.Min(t.Width,t.Height)/2);canvas.DrawRoundRect(rect,radius,radius,paint);}
    }
}
