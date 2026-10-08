using Compositor.Core;
using SkiaSharp;
namespace Compositor.Imaging;

/// <summary>Layer-pixel sigma, transparent edge spread, document-space selection and immutable results.</summary>
public static class GaussianBlur
{
    public static Document Apply(Document document,Guid layerId,double radius,int padding=0,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();document.Validate();
        if(!double.IsFinite(radius)||radius<.1||radius>250||padding<0||padding>752)throw new ArgumentOutOfRangeException(nameof(radius));
        var source=document.Layers.First(l=>l.Id==layerId);
        if(source.IsGroup||source.IsAdjustment)throw new InvalidOperationException("Select image pixels to blur.");
        var selection=document.Selection is null?null:SelectionCoverage.Create(document.Selection,document.Width,document.Height);
        if(selection?.IsEmpty==true||source.Pixels.Tiles.Count==0)return document;
        int margin=Math.Max(padding,(int)Math.Ceiling(radius*3+2));
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
        using var output=new SKBitmap(CanvasRenderer.Info(width,height));
        using(var canvas=new SKCanvas(output))
        using(var filter=SKImageFilter.CreateBlur((float)radius,(float)radius,SKShaderTileMode.Decal))
        using(var paint=new SKPaint{ImageFilter=filter,BlendMode=SKBlendMode.Src})
        {canvas.Clear(SKColors.Transparent);canvas.DrawBitmap(input,0,0,new SKSamplingOptions(SKFilterMode.Nearest),paint);}
        cancellation.ThrowIfCancellationRequested();
        var pixels=Raster.FromRgba(width,height,output.GetPixelSpan());
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
