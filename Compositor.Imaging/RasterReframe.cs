using System.Collections.Immutable;
using Compositor.Core;

namespace Compositor.Imaging;

public readonly record struct RasterFrame(int Left,int Top,int Width,int Height)
{
    public void Validate()
    {
        Limits.CheckDimensions(Width,Height);
        if(Math.Abs((long)Left)>1_000_000||Math.Abs((long)Top)>1_000_000)
            throw new InvalidDataException("Invalid raster frame origin.");
    }
}

/// <summary>Changes a layer's pixel bounds while keeping every source pixel at the same document position.</summary>
public static class RasterReframe
{
    public static Raster Apply(Raster source,RasterFrame frame,bool mask=false,CancellationToken cancellation=default)
    {
        frame.Validate();cancellation.ThrowIfCancellationRequested();
        if(frame.Left==0&&frame.Top==0&&frame.Width==source.Width&&frame.Height==source.Height)return source;
        var tiles=ImmutableDictionary.CreateBuilder<TileKey,PixelTile>();
        byte[]? white=null;PixelTile? whiteTile=null;
        if(mask){white=new byte[PixelTile.ByteCount];Array.Fill(white,(byte)255);whiteTile=new(white);}
        for(int ty=0;ty*256<frame.Height;ty++)for(int tx=0;tx*256<frame.Width;tx++)
        {
            cancellation.ThrowIfCancellationRequested();
            int left=frame.Left+tx*256,top=frame.Top+ty*256;
            int width=Math.Min(256,frame.Width-tx*256),height=Math.Min(256,frame.Height-ty*256);
            bool inside=left>=0&&top>=0&&left+width<=source.Width&&top+height<=source.Height;
            if(inside&&left%256==0&&top%256==0)
            {
                if(source.Tiles.TryGetValue(new(left/256,top/256),out var shared))tiles[new(tx,ty)]=shared;
                else if(mask)throw new InvalidDataException("Mask has missing pixels.");
                continue;
            }
            if(left>=source.Width||top>=source.Height||left+width<=0||top+height<=0)
            {if(mask)tiles[new(tx,ty)]=whiteTile!;continue;}
            var buffer=mask?(byte[])white!.Clone():new byte[PixelTile.ByteCount];
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                int sx=left+x,sy=top+y;if(sx<0||sy<0||sx>=source.Width||sy>=source.Height)continue;
                int p=(y*256+x)*4;
                if(source.Tiles.TryGetValue(new(sx/256,sy/256),out var tile))
                    tile.Bytes.Slice(((sy%256)*256+sx%256)*4,4).CopyTo(buffer.AsSpan(p,4));
                else if(mask)throw new InvalidDataException("Mask has missing pixels.");
            }
            if(mask||buffer.AsSpan().IndexOfAnyExcept((byte)0)>=0)tiles[new(tx,ty)]=new(buffer);
        }
        return new(frame.Width,frame.Height,tiles.ToImmutable());
    }
    public static Layer Apply(Layer source,RasterFrame frame,CancellationToken cancellation=default)
    {
        if(source.IsAdjustment||source.IsGroup)throw new InvalidOperationException("Select an image layer to change its pixel bounds.");
        frame.Validate();
        cancellation.ThrowIfCancellationRequested();
        if(frame.Left==0&&frame.Top==0&&frame.Width==source.Pixels.Width&&frame.Height==source.Pixels.Height)return source;
        var center=source.Transform.ToDocument(new(frame.Left+frame.Width/2d,frame.Top+frame.Height/2d),source.Pixels.Width,source.Pixels.Height);
        double width=source.Transform.Width*frame.Width/source.Pixels.Width,height=source.Transform.Height*frame.Height/source.Pixels.Height;
        var transform=source.Transform with{X=center.X-width/2,Y=center.Y-height/2,Width=width,Height=height};
        transform.Validate();
        var pixels=Apply(source.Pixels,frame,cancellation:cancellation);
        LayerMask? mask=source.Mask;
        if(mask is not null&&(frame.Left!=0||frame.Top!=0||frame.Width!=source.Pixels.Width||frame.Height!=source.Pixels.Height))
            mask=mask.Placement is not null || !mask.Linked
                ? mask with{Placement=mask.Placement??source.Transform}
                : mask.WithPixels(Apply(mask.EditingPixels(source.Pixels.Width,source.Pixels.Height),frame,true,cancellation));
        return ReferenceEquals(pixels,source.Pixels)&&ReferenceEquals(mask,source.Mask)&&transform==source.Transform
            ?source:source with{Pixels=pixels,Mask=mask,Transform=transform};
    }
}
