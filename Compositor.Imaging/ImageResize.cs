using System.Collections.Immutable;
using Compositor.Core;
using SkiaSharp;
namespace Compositor.Imaging;

public sealed record ImageSizeOptions(int Width,int Height,double Resolution,Sampling Sampling=Sampling.High)
{
    public void Validate()
    {
        Limits.CheckDimensions(Width,Height);
        if(!double.IsFinite(Resolution)||Resolution is <1 or >9600||!Enum.IsDefined(Sampling))
            throw new InvalidDataException("Use 1–9,600 pixels/inch and a supported sampling method.");
    }
}
public static class ImageResize
{
    private sealed record Plan(Layer Source,LayerTransform Transform,bool Image,bool Mask,int Width,int Height);
    public static Document Apply(Document original,ImageSizeOptions options,CancellationToken cancellationToken=default)
    {
        options.Validate();cancellationToken.ThrowIfCancellationRequested();
        if(original.Width==options.Width&&original.Height==options.Height)
            return original.Resolution==options.Resolution?original:original with{Resolution=options.Resolution,Selection=null};
        double sx=(double)options.Width/original.Width,sy=(double)options.Height/original.Height;
        var plans=new List<Plan>();long used=0,usedMasks=0;
        // Preflight every transformed allocation before doing any raster work.
        foreach(var layer in original.Layers)
        {
            var t=layer.Transform;
            PointD[] unit=[new(0,0),new(1,0),new(1,1),new(0,1)];
            var corners=unit.Select(p=>t.ToDocument(p,1,1)).Select(p=>new PointD(p.X*sx,p.Y*sy)).ToArray();
            double left=Math.Floor(corners.Min(p=>p.X)),top=Math.Floor(corners.Min(p=>p.Y));
            double width=Math.Ceiling(corners.Max(p=>p.X))-left,height=Math.Ceiling(corners.Max(p=>p.Y))-top;
            var next=new LayerTransform(left,top,width,height,Sampling:options.Sampling);next.Validate();
            bool image=!layer.IsAdjustment&&!layer.IsGroup&&(layer.Pixels.Tiles.Count!=0||layer.Mask is not null);
            bool mask=layer.Mask is {Placement:null} m&&(m.Pixels.Width!=1||m.Pixels.Height!=1);
            if(image||mask)Limits.CheckDimensions((int)width,(int)height);
            if(image)used+=(long)width*(int)height;
            if(mask)usedMasks+=(long)width*(int)height;
            else if(layer.Mask is {} retainedMask)usedMasks+=(long)retainedMask.Pixels.Width*retainedMask.Pixels.Height;
            if(used>Limits.MaxPixels||usedMasks>Limits.MaxPixels)throw new InvalidDataException("Resized layers exceed the 100 megapixel image or mask limit.");
            plans.Add(new(layer,next,image,mask,(int)width,(int)height));
        }
        var layers=ImmutableArray.CreateBuilder<Layer>();
        foreach(var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var layer=plan.Source;
            var pixels=plan.Image?Resample(layer.Pixels,layer.Transform,plan,sx,sy,options.Sampling,false,cancellationToken):
                plan.Mask?new Raster(plan.Width,plan.Height):layer.Pixels;
            var mask=plan.Mask?layer.Mask!.WithPixels(Resample(layer.Mask.Pixels,layer.Transform,plan,sx,sy,options.Sampling,true,cancellationToken)):layer.Mask;
            if(mask?.Placement is {} placement)
                mask=mask with{Placement=GroupTransform.Following(placement,new(0,0,original.Width,original.Height),new(0,0,options.Width,options.Height))};
            layers.Add(layer with{Pixels=pixels,Mask=mask,Transform=plan.Transform});
        }
        var result=original with{Width=options.Width,Height=options.Height,Resolution=options.Resolution,Layers=layers.ToImmutable(),Selection=null};
        result.Validate();cancellationToken.ThrowIfCancellationRequested();
        if(EditorSession.UndoBytesRequired(original,result)>EditorSession.MaxHistoryBytes)
            throw new InvalidOperationException("Image resize exceeds the 256 MiB Undo limit.");
        return result;
    }
    private static Raster Resample(Raster source,LayerTransform t,Plan plan,double sx,double sy,Sampling sampling,bool mask,CancellationToken cancellationToken)
    {
        if(!mask&&source.Tiles.Count==0)return new Raster(plan.Width,plan.Height);
        using var input=new SKBitmap(CanvasRenderer.Info(source.Width,source.Height));
        var span=input.GetPixelSpan();span.Clear();
        foreach(var (key,tile) in source.Tiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int w=Math.Min(256,source.Width-key.X*256),h=Math.Min(256,source.Height-key.Y*256);
            for(int row=0;row<h;row++)tile.Bytes.Slice(row*PixelTile.Stride,w*4)
                .CopyTo(span.Slice(((key.Y*256+row)*source.Width+key.X*256)*4,w*4));
        }
        input.SetImmutable();using var image=SKImage.FromBitmap(input);
        using var output=new SKBitmap(CanvasRenderer.Info(plan.Width,plan.Height));
        using(var canvas=new SKCanvas(output))
        {
            canvas.Clear(mask?SKColors.Black:SKColors.Transparent);
            canvas.Translate((float)-plan.Transform.X,(float)-plan.Transform.Y);canvas.Scale((float)sx,(float)sy);
            canvas.Translate((float)(t.X+t.Width/2),(float)(t.Y+t.Height/2));canvas.RotateDegrees((float)t.Rotation);
            canvas.Scale((float)(t.Width/source.Width*(t.FlipX?-1:1)),(float)(t.Height/source.Height*(t.FlipY?-1:1)));
            canvas.Translate(-source.Width/2f,-source.Height/2f);
            var filter=sampling switch{Sampling.Nearest=>new SKSamplingOptions(SKFilterMode.Nearest),
                Sampling.Smooth=>new SKSamplingOptions(SKFilterMode.Linear),_=>new SKSamplingOptions(SKCubicResampler.CatmullRom)};
            using var paint=new SKPaint{IsAntialias=true};
            canvas.DrawImage(image,0,0,filter,paint);canvas.Flush();
        }
        cancellationToken.ThrowIfCancellationRequested();
        var raster=Raster.FromRgba(plan.Width,plan.Height,output.GetPixelSpan());
        if(!mask)return raster;
        // LayerMask requires opaque padding as well as opaque in-bounds pixels.
        var tiles=raster.Tiles.ToBuilder();
        foreach(var (key,tile) in raster.Tiles)
        {
            var bytes=tile.Bytes.ToArray();
            for(int i=3;i<bytes.Length;i+=4)bytes[i]=255;
            tiles[key]=new(bytes);
        }
        return new Raster(plan.Width,plan.Height,tiles.ToImmutable());
    }
}
