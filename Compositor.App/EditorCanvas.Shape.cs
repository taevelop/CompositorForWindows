using System.Windows.Input;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
namespace Compositor.App;
public sealed partial class EditorCanvas
{
    private ShapeDrag? shapeDrag;
    private ShapeDraft? shapeDraft;
    private sealed record Preparation(EditorSession Session,Document Original,long Generation,CancellationTokenSource Cancellation);
    private Preparation? shapePreparation;
    private readonly SemaphoreSlim shapeWorkerGate=new(1,1);
    internal bool IsShapePreparing=>shapePreparation is not null;
    internal Task<bool> ShapeCompletion {get;private set;}=Task.FromResult(true);
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
        var draft=shapeDraft;shapeDraft=null;shapeDrag=null;
        if(commit&&draft is not null&&draft.Transform.Width*draft.Transform.Height>=1_000_000)
        {
            var owner=new Preparation(Session,Session.Document,Session.TransactionGeneration,new());
            shapePreparation=owner;gestureButton=null;ShapeCompletion=CompleteShapeAsync(owner,draft,Session.ActiveLayerId);InvalidateVisual();return;
        }
        Session.Cancel();
        try{if(commit&&draft is not null)ShapeInsert.Add(Session,draft);}
        finally{gestureButton=null;InvalidateVisual();}
    }
    private static bool Owns(Preparation owner)=>owner.Session.InTransaction&&owner.Session.TransactionGeneration==owner.Generation&&ReferenceEquals(owner.Session.Document,owner.Original);
    private void CancelShapePreparation()
    {
        if(shapePreparation is not {} owner)return;
        shapePreparation=null;owner.Cancellation.Cancel();if(Owns(owner))owner.Session.Cancel();
        if(!canvasDisposed)InvalidateVisual();
    }
    private async Task<bool> CompleteShapeAsync(Preparation owner,ShapeDraft draft,Guid? active)
    {
        var token=owner.Cancellation.Token;
        try
        {
            var prepared=await Task.Run(async()=>
            {
                await shapeWorkerGate.WaitAsync(token);
                try{return ShapeInsert.Prepare(owner.Original,active,draft,token);}
                finally{shapeWorkerGate.Release();}
            });
            if(canvasDisposed||!ReferenceEquals(shapePreparation,owner)||!ReferenceEquals(Session,owner.Session)||!Owns(owner)||token.IsCancellationRequested)return false;
            owner.Session.ActiveLayerId=prepared.LayerId;owner.Session.EditMask=false;
            owner.Session.Preview(prepared.Document);owner.Session.Commit();return true;
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){return false;}
        catch(Exception error){if(ReferenceEquals(shapePreparation,owner)&&!canvasDisposed)ReportError?.Invoke(error.Message);return false;}
        finally
        {
            if(owner.Session.InTransaction&&owner.Session.TransactionGeneration==owner.Generation)owner.Session.Cancel();
            if(ReferenceEquals(shapePreparation,owner))shapePreparation=null;
            owner.Cancellation.Dispose();if(!canvasDisposed)InvalidateVisual();
        }
    }
    private void DrawShapeStatus(SKCanvas canvas)
    {
        if(!IsShapePreparing)return;
        using var background=new SKPaint{Color=new SKColor(28,30,34,230)};
        using var ink=new SKPaint{Color=new SKColor(220,230,245),IsAntialias=true};using var font=new SKFont{Size=13};
        canvas.DrawRoundRect(new SKRect(12,12,300,44),4,4,background);
        canvas.DrawText("Creating shape… · Esc to cancel",22,33,SKTextAlign.Left,font,ink);
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
