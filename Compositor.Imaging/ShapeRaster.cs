using System.Runtime.InteropServices;
using Compositor.Core;
using SkiaSharp;
namespace Compositor.Imaging;

public static class ShapeRaster
{
    public static Raster Create(LayerShapeStyle style,double width,double height,CancellationToken cancellation=default)
    {
        ArgumentNullException.ThrowIfNull(style);style.Validate();cancellation.ThrowIfCancellationRequested();
        if(!double.IsFinite(width)||!double.IsFinite(height)||width<1||height<1||width>30_000||height>30_000)
            throw new InvalidDataException("Invalid shape dimensions.");
        int pixelWidth=(int)width,pixelHeight=(int)height;Limits.CheckDimensions(pixelWidth,pixelHeight);
        using var bitmap=new SKBitmap(CanvasRenderer.Info(pixelWidth,pixelHeight));
        using var canvas=new SKCanvas(bitmap);canvas.Clear(SKColors.Transparent);
        using var paint=new SKPaint{IsAntialias=true,Color=new SKColor((byte)Math.Round(style.Red*255),(byte)Math.Round(style.Green*255),(byte)Math.Round(style.Blue*255))};
        var bounds=new SKRect(0,0,(float)width,(float)height);
        switch(style.Kind)
        {
            case ShapeKind.Rectangle:
                float radius=(float)Math.Min(style.CornerRadius,Math.Min(width,height)/2);
                canvas.DrawRoundRect(bounds,radius,radius,paint);break;
            case ShapeKind.Ellipse:canvas.DrawOval(bounds,paint);break;
            case ShapeKind.Line:
                double thickness=Math.Max(1,style.LineWidth??0);
                var start=style.Start is {} a?new SKPoint((float)(a.X*width),(float)(a.Y*height)):new SKPoint((float)(Math.Min(thickness,width)/2),(float)(Math.Min(thickness,height)/2));
                var end=style.End is {} b?new SKPoint((float)(b.X*width),(float)(b.Y*height)):new SKPoint((float)(width-Math.Min(thickness,width)/2),(float)(height-Math.Min(thickness,height)/2));
                paint.Style=SKPaintStyle.Stroke;paint.StrokeWidth=(float)thickness;paint.StrokeCap=SKStrokeCap.Round;canvas.DrawLine(start,end,paint);break;
        }
        cancellation.ThrowIfCancellationRequested();
        var bytes=new byte[checked(pixelWidth*pixelHeight*4)];Marshal.Copy(bitmap.GetPixels(),bytes,0,bytes.Length);
        cancellation.ThrowIfCancellationRequested();var raster=Raster.FromRgba(pixelWidth,pixelHeight,bytes);
        cancellation.ThrowIfCancellationRequested();return raster;
    }
}
