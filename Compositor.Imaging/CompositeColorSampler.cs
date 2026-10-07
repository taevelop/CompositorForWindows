using Compositor.Core;
using SkiaSharp;
namespace Compositor.Imaging;
public readonly record struct SampledColor(byte Red,byte Green,byte Blue);
/// <summary>Samples the visible sRGB composite at one document pixel, without checkerboard or editor overlays.</summary>
public sealed class CompositeColorSampler:IDisposable
{
    private readonly CanvasRenderer renderer=new();
    private readonly SKBitmap bitmap=new(CanvasRenderer.Info(1,1));
    private readonly SKCanvas canvas;
    private bool disposed;
    public CompositeColorSampler(){canvas=new(bitmap);}
    public SampledColor? Sample(Document document,PointD point)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(!double.IsFinite(point.X)||!double.IsFinite(point.Y)||point.X<0||point.Y<0||point.X>=document.Width||point.Y>=document.Height)return null;
        canvas.Clear(SKColors.Transparent);canvas.Save();
        try{canvas.Translate(-(float)Math.Floor(point.X),-(float)Math.Floor(point.Y));renderer.Draw(canvas,document);canvas.Flush();}
        finally{canvas.Restore();}
        var pixel=bitmap.Bytes;int alpha=pixel[3];if(alpha==0)return null;
        byte Channel(byte value)=>(byte)Math.Round(Math.Min(alpha,(int)value)*255d/alpha,MidpointRounding.AwayFromZero);
        return new(Channel(pixel[0]),Channel(pixel[1]),Channel(pixel[2]));
    }
    public void Dispose(){if(disposed)return;disposed=true;canvas.Dispose();bitmap.Dispose();renderer.Dispose();}
}
