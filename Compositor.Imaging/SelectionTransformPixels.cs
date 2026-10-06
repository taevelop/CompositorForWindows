using Compositor.Core;
using SkiaSharp;
namespace Compositor.Imaging;

/// <summary>Prepared immutable selected pixels; every preview is computed from the original document.</summary>
public sealed class SelectionTransformPixels
{
    public Document Original {get;}
    public LayerTransform InitialTransform {get;}
    private readonly Layer source;
    private readonly Raster lifted,cut;
    private SelectionTransformPixels(Document original,Layer layer,PixelClipboardContent content,Raster cutPixels)
    {
        Original=original;source=layer;lifted=content.Pixels;cut=cutPixels;
        InitialTransform=new(content.Origin.X,content.Origin.Y,lifted.Width,lifted.Height);
    }
    public static SelectionTransformPixels? Create(Document document,Guid active,bool editMask=false,CancellationToken cancellation=default)
    {
        if(editMask)throw new InvalidOperationException("Selected pixel transforms require the image target.");
        if(document.Selection is null or {IsEmpty:true})return null;
        var layer=document.Layers.First(l=>l.Id==active);
        if(layer.IsGroup||layer.IsAdjustment||!LayerHierarchy.Entries(document).First(e=>e.Layer.Id==active).Visible)
            throw new InvalidOperationException("Select a visible image layer to transform pixels.");
        cancellation.ThrowIfCancellationRequested();
        var content=SelectionClipboardPixels.Copy(document,active);
        if(content is null)return null;
        var coverage=SelectionCoverage.Create(document.Selection,document.Width,document.Height);
        var cut=SelectionPixels.Blend(layer.Pixels,new(layer.Pixels.Width,layer.Pixels.Height),layer.Transform,coverage,cancellation);
        return new(document,layer,content,cut);
    }
    private static SKMatrix Matrix(Func<PointD,PointD> map)
    {
        var p=map(new(0,0));var x=map(new(1,0));var y=map(new(0,1));
        return new SKMatrix{ScaleX=(float)(x.X-p.X),SkewX=(float)(y.X-p.X),TransX=(float)p.X,
            SkewY=(float)(x.Y-p.Y),ScaleY=(float)(y.Y-p.Y),TransY=(float)p.Y,Persp2=1};
    }
    public Document Apply(LayerTransform transform,CancellationToken cancellation=default)
    {
        transform.Validate();cancellation.ThrowIfCancellationRequested();
        if(transform==InitialTransform)return Original;
        PointD ToDoc(PointD p)=>transform.ToDocument(p,lifted.Width,lifted.Height);
        PointD ToSource(PointD p)=>source.Transform.ToPixels(p,source.Pixels.Width,source.Pixels.Height);
        PointD[] corners=[new(0,0),new(lifted.Width,0),new(lifted.Width,lifted.Height),new(0,lifted.Height)];
        var placed=corners.Select(ToDoc).ToArray();
        double dl=placed.Min(p=>p.X),dt=placed.Min(p=>p.Y),dr=placed.Max(p=>p.X),db=placed.Max(p=>p.Y);
        // Match the original's document bounding box followed by inverse layer placement.
        var bounds=new[]{new PointD(dl,dt),new PointD(dr,dt),new PointD(dr,db),new PointD(dl,db)}.Select(ToSource).ToArray();
        double left=Math.Floor(Math.Min(0,bounds.Min(p=>p.X))),top=Math.Floor(Math.Min(0,bounds.Min(p=>p.Y)));
        double right=Math.Ceiling(Math.Max(source.Pixels.Width,bounds.Max(p=>p.X))),bottom=Math.Ceiling(Math.Max(source.Pixels.Height,bounds.Max(p=>p.Y)));
        if(!double.IsFinite(left)||!double.IsFinite(top)||Math.Abs(left)>1_000_000||Math.Abs(top)>1_000_000||
            right-left>30000||bottom-top>30000)throw new InvalidDataException("The transformed selection exceeds supported layer bounds.");
        var frame=new RasterFrame((int)left,(int)top,(int)(right-left),(int)(bottom-top));frame.Validate();
        long count=(long)frame.Width*frame.Height;
        long other=Original.Layers.Where(l=>l.Id!=source.Id&&!l.IsAdjustment&&(l.Pixels.Tiles.Count!=0||l.Mask is not null)).Sum(l=>(long)l.Pixels.Width*l.Pixels.Height);
        long otherMasks=Original.Layers.Where(l=>l.Id!=source.Id&&l.Mask is not null).Sum(l=>(long)l.Mask!.Pixels.Width*l.Mask.Pixels.Height);
        bool grows=frame.Left!=0||frame.Top!=0||frame.Width!=source.Pixels.Width||frame.Height!=source.Pixels.Height;
        long maskCount=source.Mask is null?0:grows?count:(long)source.Mask.Pixels.Width*source.Mask.Pixels.Height;
        if(other+count>Limits.MaxPixels||otherMasks+maskCount>Limits.MaxPixels)
            throw new InvalidDataException("The transformed selection exceeds the project pixel budget.");
        var reframed=RasterReframe.Apply(source with{Pixels=cut},frame,cancellation);
        using var output=Bitmap(reframed.Pixels);using var input=Bitmap(lifted);
        input.SetImmutable();using var image=SKImage.FromBitmap(input);
        using(var canvas=new SKCanvas(output))
        {
            var matrix=Matrix(p=>{var local=ToSource(ToDoc(p));return new(local.X-frame.Left,local.Y-frame.Top);});
            canvas.SetMatrix(matrix);
            var sampling=transform.Sampling switch{Sampling.Nearest=>new SKSamplingOptions(SKFilterMode.Nearest),
                Sampling.Smooth=>new SKSamplingOptions(SKFilterMode.Linear),_=>new SKSamplingOptions(SKCubicResampler.CatmullRom)};
            using var paint=new SKPaint{IsAntialias=true};
            canvas.DrawImage(image,0,0,sampling,paint);canvas.Flush();
        }
        cancellation.ThrowIfCancellationRequested();
        var pixels=Raster.FromRgba(frame.Width,frame.Height,output.GetPixelSpan());
        using var path=SelectionGeometry.Path(Original.Selection!);using var builder=new SKPathBuilder{FillType=path.FillType};
        var selectionMatrix=Matrix(p=>ToDoc(InitialTransform.ToPixels(p,lifted.Width,lifted.Height)));
        builder.AddPath(path,selectionMatrix);using var moved=builder.Detach();
        var selection=Original.Selection! with{PathData=moved.ToSvgPathData()};
        var next=Original.Replace(reframed with{Pixels=pixels}) with{Selection=selection};next.Validate();
        if(EditorSession.UndoBytesRequired(Original,next)>EditorSession.MaxHistoryBytes)
            throw new InvalidOperationException("This transform exceeds the 256 MiB Undo limit.");
        return next;
    }
    private static SKBitmap Bitmap(Raster raster)
    {
        var bitmap=new SKBitmap(CanvasRenderer.Info(raster.Width,raster.Height));var bytes=bitmap.GetPixelSpan();bytes.Clear();
        foreach(var (key,tile) in raster.Tiles)
        for(int y=0;y<Math.Min(256,raster.Height-key.Y*256);y++)
        {
            int width=Math.Min(256,raster.Width-key.X*256);
            tile.Bytes.Slice(y*PixelTile.Stride,width*4).CopyTo(bytes.Slice(((key.Y*256+y)*raster.Width+key.X*256)*4,width*4));
        }
        return bitmap;
    }
}
