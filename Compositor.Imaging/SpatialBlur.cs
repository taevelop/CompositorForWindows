using Compositor.Core;
using SkiaSharp;
namespace Compositor.Imaging;

/// <summary>Layer-pixel sigma, transparent edge spread, document-space selection and immutable results.</summary>
public static class SpatialBlur
{
    public static Document Preview(Document document,Guid layerId,BlurSettings settings,int padding=0,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();document.Validate();
        settings.Validate();if(padding<0||padding>settings.MaximumPadding)throw new ArgumentOutOfRangeException(nameof(padding));
        var source=document.Layers.First(l=>l.Id==layerId);
        if(source.IsGroup||source.IsAdjustment)throw new InvalidOperationException("Select image pixels to blur.");
        int margin=Math.Max(padding,settings.Margin);
        int width=checked(source.Pixels.Width+margin*2),height=checked(source.Pixels.Height+margin*2);Limits.CheckDimensions(width,height);
        if(Math.Max(width,height)<=2048)return Apply(document,layerId,settings,padding,cancellation);
        long other=document.Layers.Where(l=>l.Id!=layerId&&!l.IsAdjustment&&(l.Pixels.Tiles.Count!=0||l.Mask is not null)).Sum(l=>(long)l.Pixels.Width*l.Pixels.Height);
        if(other+(long)width*height>Limits.MaxPixels)throw new InvalidDataException("Blur padding exceeds the document pixel budget.");
        var selection=document.Selection is null?null:SelectionCoverage.Create(document.Selection,document.Width,document.Height);
        if(selection?.IsEmpty==true||source.Pixels.Tiles.Count==0)return document;
        double factor=2048d/Math.Max(width,height);int w=Math.Max(1,(int)(width*factor)),h=Math.Max(1,(int)(height*factor));
        using var input=new SKBitmap(CanvasRenderer.Info(w,h));var bytes=input.GetPixelSpan();
        for(int y=0;y<h;y++)
        {
            cancellation.ThrowIfCancellationRequested();
            double sy=(y+.5)*height/h-margin-.5;int top=(int)Math.Floor(sy);double fy=sy-top;
            for(int x=0;x<w;x++)
            {
                double sx=(x+.5)*width/w-margin-.5;int left=(int)Math.Floor(sx);double fx=sx-left;
                var a0=Sample(source.Pixels,left,top);var a1=Sample(source.Pixels,left+1,top);
                var b0=Sample(source.Pixels,left,top+1);var b1=Sample(source.Pixels,left+1,top+1);
                for(int c=0;c<4;c++)
                {
                    double a=a0[c]*(1-fx)+a1[c]*fx;
                    double b=b0[c]*(1-fx)+b1[c]*fx;
                    bytes[(y*w+x)*4+c]=(byte)Math.Clamp(Math.Round(a*(1-fy)+b*fy),0,255);
                }
            }
        }
        var small=Raster.FromRgba(w,h,bytes);var blurred=BlurPixels(input,settings,(double)w/width,cancellation);
        var center=source.Transform.ToDocument(new(source.Pixels.Width/2d,source.Pixels.Height/2d),source.Pixels.Width,source.Pixels.Height);
        double placedWidth=source.Transform.Width*width/source.Pixels.Width,placedHeight=source.Transform.Height*height/source.Pixels.Height;
        var transform=source.Transform with{X=center.X-placedWidth/2,Y=center.Y-placedHeight/2,Width=placedWidth,Height=placedHeight};transform.Validate();
        var pixels=SelectionPixels.Blend(small,blurred,transform,selection,cancellation);
        var mask=source.Mask;
        if(mask is {Placement:null}&&(mask.Pixels.Width!=1||mask.Pixels.Height!=1))mask=mask with{Placement=source.Transform};
        cancellation.ThrowIfCancellationRequested();return document.Replace(source with{Pixels=pixels,Transform=transform,Mask=mask});
    }
    private static readonly byte[] transparent=[0,0,0,0];
    private static ReadOnlySpan<byte> Sample(Raster raster,int x,int y)=>x<0||y<0||x>=raster.Width||y>=raster.Height?transparent:
        raster.Tiles.TryGetValue(new(x/256,y/256),out var tile)?tile.Bytes.Slice(((y%256)*256+x%256)*4,4):transparent;
    private static Raster BlurPixels(SKBitmap input,BlurSettings settings,double scale,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if(settings.Kind==BlurKind.Motion)return MotionBlurRaster.Apply(input,settings.Amount*scale,settings.Angle,cancellation);
        using var output=new SKBitmap(input.Info);
        using(var canvas=new SKCanvas(output))
        using(var filter=SKImageFilter.CreateBlur((float)(settings.Amount*scale),(float)(settings.Amount*scale),SKShaderTileMode.Decal))
        using(var paint=new SKPaint{ImageFilter=filter,BlendMode=SKBlendMode.Src})
        {canvas.Clear(SKColors.Transparent);canvas.DrawBitmap(input,0,0,new SKSamplingOptions(SKFilterMode.Nearest),paint);}
        cancellation.ThrowIfCancellationRequested();var result=Raster.FromRgba(input.Width,input.Height,output.GetPixelSpan());
        cancellation.ThrowIfCancellationRequested();return result;
    }
    public static Document Apply(Document document,Guid layerId,BlurSettings settings,int padding=0,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();document.Validate();
        settings.Validate();if(padding<0||padding>settings.MaximumPadding)throw new ArgumentOutOfRangeException(nameof(padding));
        var source=document.Layers.First(l=>l.Id==layerId);
        if(source.IsGroup||source.IsAdjustment)throw new InvalidOperationException("Select image pixels to blur.");
        var selection=document.Selection is null?null:SelectionCoverage.Create(document.Selection,document.Width,document.Height);
        if(selection?.IsEmpty==true||source.Pixels.Tiles.Count==0)return document;
        int margin=Math.Max(padding,settings.Margin);
        int width=checked(source.Pixels.Width+margin*2),height=checked(source.Pixels.Height+margin*2);
        Limits.CheckDimensions(width,height);
        long other=document.Layers.Where(l=>l.Id!=layerId&&!l.IsAdjustment&&(l.Pixels.Tiles.Count!=0||l.Mask is not null)).Sum(l=>(long)l.Pixels.Width*l.Pixels.Height);
        if(other+(long)width*height>Limits.MaxPixels)throw new InvalidDataException("Blur padding exceeds the document pixel budget.");
        // Preserve the original mask grid and edge-tone extrapolation, independently of the enlarged image grid.
        var anchored=source.Mask is {Placement:null} mask&&(mask.Pixels.Width!=1||mask.Pixels.Height!=1)
            ?source with{Mask=mask with{Placement=source.Transform}}:source;
        var grown=RasterReframe.Apply(anchored,new(-margin,-margin,width,height),cancellation);
        using var input=new SKBitmap(CanvasRenderer.Info(width,height));grown.Pixels.ToRgba().CopyTo(input.GetPixelSpan());
        cancellation.ThrowIfCancellationRequested();
        var pixels=BlurPixels(input,settings,1,cancellation);
        pixels=SelectionPixels.Blend(grown.Pixels,pixels,grown.Transform,selection,cancellation);
        if(ReferenceEquals(pixels,grown.Pixels))return document;
        var result=grown with{Pixels=pixels};
        int left=width,top=height,right=0,bottom=0;
        foreach(var (key,tile) in pixels.Tiles)
        {
            cancellation.ThrowIfCancellationRequested();
            for(int y=0;y<Math.Min(256,height-key.Y*256);y++)for(int x=0;x<Math.Min(256,width-key.X*256);x++)
                if(tile.Bytes[(y*256+x)*4+3]!=0){int px=key.X*256+x,py=key.Y*256+y;left=Math.Min(left,px);top=Math.Min(top,py);right=Math.Max(right,px+1);bottom=Math.Max(bottom,py+1);}
        }
        if(right>left&&bottom>top)result=RasterReframe.Apply(result,new(left,top,right-left,bottom-top),cancellation);
        var next=document.Replace(result);next.Validate();
        if(EditorSession.UndoBytesRequired(document,next)>EditorSession.MaxHistoryBytes)throw new InvalidOperationException("Blur exceeds the 256 MiB Undo limit.");
        cancellation.ThrowIfCancellationRequested();return next;
    }
}
