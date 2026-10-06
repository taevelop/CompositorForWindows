using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

/// <summary>Immutable grayscale coverage for a bounded document region. An empty instance clips every pixel.</summary>
public sealed class SelectionCoverage
{
    private readonly byte[] pixels;
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public int CanvasWidth { get; }
    public int CanvasHeight { get; }
    public bool IsEmpty => pixels.Length == 0;
    public ReadOnlySpan<byte> Pixels => pixels;
    private SelectionCoverage(int x, int y, int width, int height, int canvasWidth, int canvasHeight, byte[] pixels)
    { X=x; Y=y; Width=width; Height=height; CanvasWidth=canvasWidth; CanvasHeight=canvasHeight; this.pixels=pixels; }
    public static SelectionCoverage Create(DocumentSelection selection, int width, int height)
    {
        Limits.CheckDimensions(width,height); using var path=SelectionGeometry.Path(selection);
        var b=path.Bounds; double margin=Math.Ceiling(selection.Feather*2)+1;
        int x=(int)Math.Clamp(Math.Floor(b.Left-margin),0,width), y=(int)Math.Clamp(Math.Floor(b.Top-margin),0,height);
        int right=(int)Math.Clamp(Math.Ceiling(b.Right+margin),0,width), bottom=(int)Math.Clamp(Math.Ceiling(b.Bottom+margin),0,height);
        if(path.IsEmpty||b.IsEmpty||right<=x||bottom<=y) return new(0,0,0,0,width,height,[]);
        int w=right-x,h=bottom-y;
        using var bitmap=new SKBitmap(CanvasRenderer.Info(w,h));
        using(var canvas=new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Black); canvas.Translate(-x,-y);
            using var paint=new SKPaint{Color=SKColors.White,IsAntialias=selection.Antialiased||selection.Feather>0};
            canvas.DrawPath(path,paint); canvas.Flush();
        }
        SKBitmap? blurred=null;
        try
        {
            if(selection.Feather>0)
            {
                blurred=new SKBitmap(CanvasRenderer.Info(w,h));using var canvas=new SKCanvas(blurred);canvas.Clear(SKColors.Black);
                using var image=SKImage.FromBitmap(bitmap);using var blur=SKImageFilter.CreateBlur((float)(selection.Feather/2),(float)(selection.Feather/2),SKShaderTileMode.Clamp);
                using var paint=new SKPaint{ImageFilter=blur};
                canvas.DrawImage(image,0,0,new SKSamplingOptions(SKFilterMode.Nearest),paint);canvas.Flush();
            }
            var bytes=(blurred??bitmap).GetPixelSpan();var gray=new byte[w*h];
            for(int i=0;i<gray.Length;i++)gray[i]=bytes[i*4];
            if(gray.AsSpan().IndexOfAnyExcept((byte)0)<0)return new(0,0,0,0,width,height,[]);
            return new(x,y,w,h,width,height,gray);
        }
        finally{blurred?.Dispose();}
    }
    public double Sample(PointD point)
    {
        if(IsEmpty||!double.IsFinite(point.X)||!double.IsFinite(point.Y)||point.X<0||point.Y<0||point.X>=CanvasWidth||point.Y>=CanvasHeight)return 0;
        double x=point.X-X-.5,y=point.Y-Y-.5;
        if(x < -1 || y < -1 || x >= Width || y >= Height)return 0;
        int left=(int)Math.Floor(x),top=(int)Math.Floor(y);double tx=x-left,ty=y-top;
        double At(int sx,int sy)
        {
            if(X==0&&sx<0)sx=0;if(Y==0&&sy<0)sy=0;
            if(X+Width==CanvasWidth&&sx>=Width)sx=Width-1;if(Y+Height==CanvasHeight&&sy>=Height)sy=Height-1;
            return sx>=0&&sy>=0&&sx<Width&&sy<Height?pixels[sy*Width+sx]/255d:0;
        }
        return (At(left,top)*(1-tx)+At(left+1,top)*tx)*(1-ty)+(At(left,top+1)*(1-tx)+At(left+1,top+1)*tx)*ty;
    }
}
