using Compositor.Core;

namespace Compositor.Imaging;

/// <summary>Immutable layer-local pixels lifted through a document-space selection.</summary>
public sealed class SelectedPixelTransfer
{
    public Raster Pixels { get; }
    public Raster CutSource { get; }
    public int Left { get; }
    public int Top { get; }
    public int Right { get; }
    public int Bottom { get; }
    private SelectedPixelTransfer(Raster pixels, Raster cutSource, int left, int top, int right, int bottom)
    { Pixels=pixels; CutSource=cutSource; Left=left; Top=top; Right=right; Bottom=bottom; }

    public static SelectedPixelTransfer? Lift(Layer layer, SelectionCoverage selection, CancellationToken cancellation=default)
    {
        if(layer.IsAdjustment||layer.IsGroup)throw new InvalidOperationException("Select an image layer to move pixels.");
        if(selection.IsEmpty)return null;
        var empty=new Raster(layer.Pixels.Width,layer.Pixels.Height);
        var lifted=SelectionPixels.Blend(empty,layer.Pixels,layer.Transform,selection,cancellation);
        if(lifted.Tiles.Count==0)return null;
        int left=lifted.Width,top=lifted.Height,right=0,bottom=0;
        foreach(var (key,tile) in lifted.Tiles)
        {
            cancellation.ThrowIfCancellationRequested();
            int width=Math.Min(256,lifted.Width-key.X*256),height=Math.Min(256,lifted.Height-key.Y*256);
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                if(tile.Bytes[(y*256+x)*4+3]!=0)
                {left=Math.Min(left,key.X*256+x);top=Math.Min(top,key.Y*256+y);right=Math.Max(right,key.X*256+x+1);bottom=Math.Max(bottom,key.Y*256+y+1);}
        }
        if(right<=left||bottom<=top)return null;
        var cut=SelectionPixels.Blend(layer.Pixels,empty,layer.Transform,selection,cancellation);
        return new(lifted,cut,left,top,right,bottom);
    }

    /// <summary>Places the original lifted pixels over a supplied base. Coordinates are source-grid pixels.
    /// The caller owns document-to-pixel conversion, destination expansion and zero-displacement no-op.</summary>
    public Raster CompositeOnto(Raster destination,double dx,double dy,Sampling sampling=Sampling.High,CancellationToken cancellation=default)
    {
        if(!double.IsFinite(dx)||!double.IsFinite(dy)||Math.Abs(dx)>1_000_000||Math.Abs(dy)>1_000_000||!Enum.IsDefined(sampling))
            throw new ArgumentOutOfRangeException(nameof(dx));
        cancellation.ThrowIfCancellationRequested();
        bool whole=Math.Abs(dx-Math.Round(dx))<1e-9&&Math.Abs(dy-Math.Round(dy))<1e-9;
        if(whole){dx=Math.Round(dx);dy=Math.Round(dy);}
        int radius=whole||sampling==Sampling.Nearest?0:sampling==Sampling.Smooth?1:2;
        int left=(int)Math.Clamp(Math.Floor(Left+dx)-radius,0,destination.Width),top=(int)Math.Clamp(Math.Floor(Top+dy)-radius,0,destination.Height);
        int right=(int)Math.Clamp(Math.Ceiling(Right+dx)+radius,0,destination.Width),bottom=(int)Math.Clamp(Math.Ceiling(Bottom+dy)+radius,0,destination.Height);
        if(right<=left||bottom<=top)return destination;
        var tiles=destination.Tiles.ToBuilder();bool changed=false;
        Span<double> rgba=stackalloc double[4];
        for(int ty=top/256;ty<=(bottom-1)/256;ty++)for(int tx=left/256;tx<=(right-1)/256;tx++)
        {
            cancellation.ThrowIfCancellationRequested();
            var key=new TileKey(tx,ty);tiles.TryGetValue(key,out var before);
            var output=before is null?new byte[PixelTile.ByteCount]:before.Bytes.ToArray();
            for(int y=Math.Max(top,ty*256);y<Math.Min(bottom,(ty+1)*256);y++)
            for(int x=Math.Max(left,tx*256);x<Math.Min(right,(tx+1)*256);x++)
            {
                Sample(x-dx,y-dy,whole?Sampling.Nearest:sampling,rgba);
                double alpha=rgba[3];if(alpha<=0)continue;int p=((y-ty*256)*256+x-tx*256)*4;
                byte a=Round(alpha+output[p+3]*(1-alpha/255));
                for(int c=0;c<3;c++)output[p+c]=(byte)Math.Min(a,Round(rgba[c]+output[p+c]*(1-alpha/255)));
                output[p+3]=a;
            }
            if(before is not null&&before.Bytes.SequenceEqual(output))continue;
            if(output.AsSpan().IndexOfAnyExcept((byte)0)<0)continue;
            tiles[key]=new(output);changed=true;
        }
        return changed?new(destination.Width,destination.Height,tiles.ToImmutable()):destination;
    }
    private static byte Round(double value)=>(byte)Math.Clamp(Math.Round(value,MidpointRounding.AwayFromZero),0,255);
    private byte Channel(int x,int y,int channel)
    {
        if(x<0||y<0||x>=Pixels.Width||y>=Pixels.Height)return 0;
        return Pixels.Tiles.TryGetValue(new(x/256,y/256),out var tile)?tile.Bytes[((y%256)*256+x%256)*4+channel]:(byte)0;
    }
    private void Sample(double x,double y,Sampling sampling,Span<double> result)
    {
        result.Clear();
        if(sampling==Sampling.Nearest)
        {for(int c=0;c<4;c++)result[c]=Channel((int)Math.Floor(x+.5),(int)Math.Floor(y+.5),c);return;}
        int ix=(int)Math.Floor(x),iy=(int)Math.Floor(y);double fx=x-ix,fy=y-iy;
        if(sampling==Sampling.Smooth)
        {
            for(int c=0;c<4;c++)result[c]=(Channel(ix,iy,c)*(1-fx)+Channel(ix+1,iy,c)*fx)*(1-fy)+(Channel(ix,iy+1,c)*(1-fx)+Channel(ix+1,iy+1,c)*fx)*fy;
        }
        else
        {
            for(int oy=-1;oy<=2;oy++)for(int ox=-1;ox<=2;ox++)
            {double weight=Cubic(ox-fx)*Cubic(oy-fy);for(int c=0;c<4;c++)result[c]+=Channel(ix+ox,iy+oy,c)*weight;}
        }
        result[3]=Math.Clamp(result[3],0,255);
        for(int c=0;c<3;c++)result[c]=Math.Clamp(result[c],0,result[3]);
    }
    private static double Cubic(double distance)
    {
        double x=Math.Abs(distance);
        return x<=1?1.5*x*x*x-2.5*x*x+1:x<2?-.5*x*x*x+2.5*x*x-4*x+2:0;
    }
}
