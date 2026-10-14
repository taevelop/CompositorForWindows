using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

public sealed record MatteSettings(bool Advanced=false,double RefineEdges=12,double Contrast=25,double ShiftEdge=0)
{
    public void Validate()
    {
        if(!double.IsFinite(RefineEdges)||RefineEdges is <0 or >40||!double.IsFinite(Contrast)||Contrast is <0 or >100||!double.IsFinite(ShiftEdge)||ShiftEdge is <-10 or >10)throw new ArgumentOutOfRangeException(nameof(RefineEdges));
    }
}
public static class MatteRefinement
{
    public static LayerMask Apply(LayerMask mask,Raster guide,MatteSettings settings,bool preview=false,CancellationToken cancellation=default)
    {
        settings.Validate();cancellation.ThrowIfCancellationRequested();
        int width=mask.Pixels.Width,height=mask.Pixels.Height;
        if(guide.Width!=width||guide.Height!=height)throw new ArgumentException("Guide and mask dimensions must match.");
        if(!settings.Advanced||settings.RefineEdges==0&&settings.Contrast==0&&settings.ShiftEdge==0)return mask;
        double factor=preview?Math.Min(1,1400d/Math.Max(width,height)):1;
        int w=Math.Max(1,(int)Math.Round(width*factor,MidpointRounding.AwayFromZero)),h=Math.Max(1,(int)Math.Round(height*factor,MidpointRounding.AwayFromZero));
        using var grayMask=Levels(mask.Pixels,w,h,cancellation);var levels=new float[w*h];var bytes=grayMask.GetPixelSpan();
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)levels[y*w+x]=bytes[y*grayMask.RowBytes+x]/255f;
        if(settings.RefineEdges>0)
        {
            using var grayGuide=Levels(guide,w,h,cancellation);var guideLevels=new float[w*h];var pixels=grayGuide.GetPixelSpan();
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)guideLevels[y*w+x]=pixels[y*grayGuide.RowBytes+x]/255f;
            levels=GuidedMatte.Filter(levels,guideLevels,w,h,Math.Max(1,(int)Math.Round(settings.RefineEdges*factor,MidpointRounding.AwayFromZero)),cancellation);
        }
        using var small=new SKBitmap(CanvasRenderer.Info(w,h));var data=small.GetPixelSpan();
        for(int i=0;i<levels.Length;i++){byte value=(byte)Math.Clamp(levels[i]*255+.5f,0,255);data[i*4]=data[i*4+1]=data[i*4+2]=value;data[i*4+3]=255;}
        using var full=new SKBitmap(CanvasRenderer.Info(width,height));
        using(var canvas=new SKCanvas(full))canvas.DrawBitmap(small,new SKRect(0,0,width,height),new SKSamplingOptions(SKCubicResampler.CatmullRom));
        using var shifted=new SKBitmap(full.Info);
        if(settings.ShiftEdge!=0)
        {
            using var canvas=new SKCanvas(shifted);using var filter=SKImageFilter.CreateBlur((float)(Math.Abs(settings.ShiftEdge)/2),(float)(Math.Abs(settings.ShiftEdge)/2),SKShaderTileMode.Clamp);
            using var paint=new SKPaint{ImageFilter=filter};canvas.DrawBitmap(full,0,0,new SKSamplingOptions(SKFilterMode.Nearest),paint);
        }
        var final=(settings.ShiftEdge==0?full:shifted).GetPixelSpan();
        double slope=1/Math.Max(.02,1-settings.Contrast/100*.98);
        for(int i=0;i<width*height;i++)
        {
            if(i%65536==0)cancellation.ThrowIfCancellationRequested();double value=final[i*4]/255d;
            if(settings.ShiftEdge!=0)value=Math.Clamp((value-(settings.ShiftEdge<0?.75:.25))/.001,0,1);
            value=Math.Clamp((value-.5)*slope+.5,0,1);byte gray=(byte)Math.Clamp(value*255+.5,0,255);
            final[i*4]=final[i*4+1]=final[i*4+2]=gray;final[i*4+3]=255;
        }
        var raster=Raster.FromRgba(width,height,final);var tiles=raster.Tiles.ToBuilder();
        foreach(var(key,tile) in raster.Tiles)
        {
            cancellation.ThrowIfCancellationRequested();var pixels=tile.Bytes.ToArray();
            for(int i=3;i<pixels.Length;i+=4)pixels[i]=255;
            tiles[key]=new(pixels);
        }
        return new(new Raster(width,height,tiles.ToImmutable()));
    }
    private static SKBitmap Levels(Raster raster,int width,int height,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();using var input=new SKBitmap(CanvasRenderer.Info(raster.Width,raster.Height));RasterBuffer.CopyTo(raster,input.GetPixelSpan(),cancellation);
        var output=new SKBitmap(new SKImageInfo(width,height,SKColorType.Gray8,SKAlphaType.Opaque));
        try{using var canvas=new SKCanvas(output);canvas.Clear(SKColors.Black);canvas.DrawBitmap(input,new SKRect(0,0,width,height),new SKSamplingOptions(SKCubicResampler.CatmullRom));cancellation.ThrowIfCancellationRequested();return output;}
        catch{output.Dispose();throw;}
    }
}

