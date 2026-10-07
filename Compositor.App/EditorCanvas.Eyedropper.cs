using System.Windows;
using System.Windows.Input;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
namespace Compositor.App;
public sealed partial class EditorCanvas
{
    internal SampledColor? SampleComposite(PointD point)=>renderer.Sample(Session.Document,point);
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
    private void SampleDocumentColor(PointD point)
    {
        if(SampleComposite(point) is {} color){samplingCurrent=color;ColorSampled?.Invoke(color);}
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