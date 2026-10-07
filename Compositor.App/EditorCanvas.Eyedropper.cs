using System.Windows;
using System.Windows.Input;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
namespace Compositor.App;
public sealed partial class EditorCanvas
{
    internal SampledColor? SampleComposite(PointD point)=>EnsurePrepared(Session.Document)?renderer.Sample(Session.Document,point):null;
    internal async Task<SampledColor?> SampleCompositeAsync(PointD point)
    {
        var targetSession=Session;var document=targetSession.Document;
        if(!EnsurePrepared(document))await RenderPreparation;
        if(canvasDisposed||!ReferenceEquals(Session,targetSession)||!ReferenceEquals(Session.Document,document)||!EnsurePrepared(document))return null;
        return renderer.Sample(document,point);
    }
    private long sampleRequest;
    internal Task SampleCompletion {get;private set;}=Task.CompletedTask;
    private bool samplingColor;
    public Action<SampledColor>? ColorSampled {get;set;}
    public Func<SampledColor> ReadSampleColor {get;set;}=()=>new(0,0,0);
    public bool ShowSampleRing {get;set;}=true;
    private SampledColor samplingOriginal,samplingCurrent;
    private Point samplingPoint;
    internal (SampledColor Original,SampledColor Current) SampleRingColors=>(samplingOriginal,samplingCurrent);
    private void BeginColorSampling(MouseButton button,Point point)
    {
        samplingOriginal=samplingCurrent=ReadSampleColor();samplingColor=true;gestureButton=button;SampleAt(point);
    }
    internal bool IsSamplingColor=>samplingColor;
    private void SampleDocumentColor(PointD point)=>SampleCompletion=ApplySampleAsync(point,++sampleRequest);
    private async Task ApplySampleAsync(PointD point,long request)
    {
        try
        {
            var color=await SampleCompositeAsync(point);
            if(request==sampleRequest&&color is {} value){samplingCurrent=value;ColorSampled?.Invoke(value);InvalidateVisual();}
        }
        catch(Exception error){if(request==sampleRequest&&!canvasDisposed)ReportError?.Invoke(error.Message);}
    }
    private void SampleAt(Point point){samplingPoint=point;Cursor=Cursors.Cross;SampleDocumentColor(DocumentPoint(point));InvalidateVisual();}
    private void EndColorSampling()
    {
        samplingColor=false;gestureButton=null;ReleaseGestureCapture();
        Cursor=Tool==EditorTool.Eyedropper?Cursors.Cross:null;InvalidateVisual();
    }
    internal void DrawSampleRing(SKCanvas canvas)
    {
        if(!samplingColor||!ShowSampleRing)return;
        float x=(float)samplingPoint.X,y=(float)samplingPoint.Y;
        using var paint=new SKPaint{IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeWidth=24,Color=new SKColor(115,115,115)};
        canvas.DrawCircle(x,y,43,paint);paint.StrokeWidth=16;
        void Half(SampledColor color,bool top)
        {
            canvas.Save();canvas.ClipRect(new SKRect(x-58,top?y-58:y,x+58,top?y:y+58));
            paint.Color=new SKColor(color.Red,color.Green,color.Blue);canvas.DrawCircle(x,y,43,paint);canvas.Restore();
        }
        Half(samplingCurrent,true);Half(samplingOriginal,false);
    }
}