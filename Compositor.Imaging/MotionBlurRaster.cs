using SkiaSharp;
using Compositor.Core;
namespace Compositor.Imaging;
internal static class MotionBlurRaster
{
    internal static Raster Apply(SKBitmap input,double distance,double angle,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();float sigma=(float)(distance/Math.Sqrt(12));
        using var output=new SKBitmap(input.Info);
        var sampling=new SKSamplingOptions(SKFilterMode.Linear);
        if(angle==0||Math.Abs(angle)==90)
        {
            using var canvas=new SKCanvas(output);
            using var filter=SKImageFilter.CreateBlur(angle==0?sigma:0,angle==0?0:sigma,SKShaderTileMode.Decal);
            using var paint=new SKPaint{ImageFilter=filter,BlendMode=SKBlendMode.Src};
            canvas.Clear(SKColors.Transparent);canvas.DrawBitmap(input,0,0,new SKSamplingOptions(SKFilterMode.Nearest),paint);
        }
        else
        {
            double radians=angle*Math.PI/180,c=Math.Abs(Math.Cos(radians)),s=Math.Abs(Math.Sin(radians));
            int width=checked((int)Math.Ceiling(input.Width*c+input.Height*s)+4);
            int height=checked((int)Math.Ceiling(input.Width*s+input.Height*c)+4);
            using var canvas=new SKCanvas(output);canvas.Clear(SKColors.Transparent);
            canvas.Translate(input.Width/2f,input.Height/2f);canvas.RotateDegrees((float)-angle);canvas.Translate(-width/2f,-height/2f);
            using var filter=SKImageFilter.CreateBlur(sigma,0,SKShaderTileMode.Decal);
            using var paint=new SKPaint{ImageFilter=filter,BlendMode=SKBlendMode.Src};
            using var copy=new SKPaint{BlendMode=SKBlendMode.Src};
            // Strip halos share the same mapping; inverse bilinear sampling crosses no hard strip edge.
            for(int top=0;top<height;top+=128)
            {
                cancellation.ThrowIfCancellationRequested();int rows=Math.Min(128,height-top);
                using var rotated=new SKBitmap(CanvasRenderer.Info(width,rows+2));
                using(var rotating=new SKCanvas(rotated))
                {
                    rotating.Clear(SKColors.Transparent);rotating.Translate(width/2f,height/2f-top+1);rotating.RotateDegrees((float)angle);rotating.Translate(-input.Width/2f,-input.Height/2f);
                    rotating.DrawBitmap(input,0,0,sampling);
                }
                using var blurred=new SKBitmap(rotated.Info);
                using(var blurring=new SKCanvas(blurred)){blurring.Clear(SKColors.Transparent);blurring.DrawBitmap(rotated,0,0,new SKSamplingOptions(SKFilterMode.Nearest),paint);}
                canvas.Save();canvas.ClipRect(new(0,top,width,top+rows),SKClipOperation.Intersect,false);
                canvas.DrawBitmap(blurred,0,top-1,sampling,copy);canvas.Restore();
            }
        }
        cancellation.ThrowIfCancellationRequested();var result=Raster.FromRgba(input.Width,input.Height,output.GetPixelSpan());
        cancellation.ThrowIfCancellationRequested();return result;
    }
}
