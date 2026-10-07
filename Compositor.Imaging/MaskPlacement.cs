using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

/// <summary>Resolves an unchanged mask grid into a layer grid without replacing its source.</summary>
public static class MaskPlacement
{
    public static LayerTransform? Move(LayerTransform? placement, bool linked, LayerTransform from, LayerTransform to)
    {
        from.Validate();to.Validate();placement?.Validate();
        var moved=linked ? placement is {} p ? GroupTransform.Following(p,from,to) : null : placement??from;
        return moved==to ? null : moved;
    }
    public static LayerMask Resolve(LayerMask mask, LayerTransform placement, LayerTransform layer,
        int width, int height, CancellationToken cancellationToken=default)
    {
        placement.Validate();layer.Validate();Limits.CheckDimensions(width,height);
        cancellationToken.ThrowIfCancellationRequested();
        if(mask.Pixels.Width==1 && mask.Pixels.Height==1) return mask;
        var source=mask.Pixels;
        using var input=new SKBitmap(new SKImageInfo(source.Width,source.Height,SKColorType.Gray8,SKAlphaType.Opaque));
        var bytes=input.GetPixelSpan();
        foreach(var (key,tile) in source.Tiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for(int y=0;y<Math.Min(256,source.Height-key.Y*256);y++)
            for(int x=0;x<Math.Min(256,source.Width-key.X*256);x++)
                bytes[(key.Y*256+y)*input.RowBytes+key.X*256+x]=tile.Bytes[(y*256+x)*4];
        }
        input.SetImmutable();using var image=SKImage.FromBitmap(input);
        // Original background rule uses the edge average of a small mask thumbnail.
        double scale=Math.Min(1,96d/Math.Max(source.Width,source.Height));
        int tw=Math.Max(1,(int)(source.Width*scale)),th=Math.Max(1,(int)(source.Height*scale));
        using var thumbnail=new SKBitmap(new SKImageInfo(tw,th,SKColorType.Gray8,SKAlphaType.Opaque));
        using(var canvas=new SKCanvas(thumbnail))
            canvas.DrawImage(image,new SKRect(0,0,tw,th),new SKSamplingOptions(SKCubicResampler.CatmullRom));
        long total=0,count=0;var thumb=thumbnail.GetPixelSpan();
        for(int y=0;y<th;y++)for(int x=0;x<tw;x++)
            if(y==0||y==th-1||x==0||x==tw-1){total+=thumb[y*thumbnail.RowBytes+x];count++;}
        using var output=new SKBitmap(CanvasRenderer.Info(width,height));
        using(var canvas=new SKCanvas(output))
        {
            canvas.Clear(total*2>=count*255?SKColors.White:SKColors.Black);
            PointD Map(PointD point)=>layer.ToPixels(placement.ToDocument(point,source.Width,source.Height),width,height);
            var origin=Map(new(0,0));var x=Map(new(1,0));var y=Map(new(0,1));
            canvas.SetMatrix(new SKMatrix((float)(x.X-origin.X),(float)(y.X-origin.X),(float)origin.X,
                (float)(x.Y-origin.Y),(float)(y.Y-origin.Y),(float)origin.Y,0,0,1));
            canvas.DrawImage(image,0,0,new SKSamplingOptions(SKCubicResampler.CatmullRom));canvas.Flush();
        }
        cancellationToken.ThrowIfCancellationRequested();
        var raster=Raster.FromRgba(width,height,output.GetPixelSpan());var tiles=raster.Tiles.ToBuilder();
        foreach(var (key,tile) in raster.Tiles)
        {
            cancellationToken.ThrowIfCancellationRequested();var data=tile.Bytes.ToArray();
            for(int i=3;i<data.Length;i+=4)data[i]=255;
            tiles[key]=new(data);
        }
        return new LayerMask(new Raster(width,height,tiles.ToImmutable()),mask.Enabled);
    }
}
