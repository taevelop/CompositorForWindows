using Compositor.Core;
namespace Compositor.Imaging;

public static class SelectionPixels
{
    public static Raster Blend(Raster original, Raster edited, LayerTransform transform, SelectionCoverage? selection, CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();transform.Validate();
        if(original.Width!=edited.Width||original.Height!=edited.Height)throw new ArgumentException("Selection blending requires matching image sizes.");
        if(selection is null)return edited;
        if(selection.IsEmpty||ReferenceEquals(original,edited))return original;
        var result=original.Tiles.ToBuilder();bool changed=false;var buffer=new byte[PixelTile.ByteCount];
        foreach(var key in original.Tiles.Keys.Union(edited.Tiles.Keys))
        {
            cancellation.ThrowIfCancellationRequested();
            original.Tiles.TryGetValue(key,out var before);edited.Tiles.TryGetValue(key,out var after);
            if(ReferenceEquals(before,after))continue;
            int width=Math.Min(256,original.Width-key.X*256),height=Math.Min(256,original.Height-key.Y*256);
            double left=key.X*256,top=key.Y*256;
            PointD[] corners=[new(left,top),new(left+width,top),new(left+width,top+height),new(left,top+height)];
            var bounds=corners.Select(p=>transform.ToDocument(p,original.Width,original.Height)).ToArray();
            if(bounds.Max(p=>p.X)<selection.X-1||bounds.Min(p=>p.X)>selection.X+selection.Width+1||
                bounds.Max(p=>p.Y)<selection.Y-1||bounds.Min(p=>p.Y)>selection.Y+selection.Height+1)continue;
            Array.Clear(buffer);if(before is not null)before.Bytes.CopyTo(buffer);
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                var point=transform.ToDocument(new(key.X*256+x+.5,key.Y*256+y+.5),original.Width,original.Height);
                double weight=selection.Sample(point);if(weight<=0)continue;int p=(y*256+x)*4;
                for(int c=0;c<4;c++)
                {
                    int a=before is null?0:before.Bytes[p+c],b=after is null?0:after.Bytes[p+c];
                    buffer[p+c]=(byte)Math.Clamp(Math.Round(a*(1-weight)+b*weight,MidpointRounding.AwayFromZero),0,255);
                }
            }
            if(before is not null&&before.Bytes.SequenceEqual(buffer))continue;
            if(buffer.AsSpan().IndexOfAnyExcept((byte)0)<0){if(before is not null){result.Remove(key);changed=true;}}
            else {result[key]=new(buffer);changed=true;}
        }
        return changed?new(original.Width,original.Height,result.ToImmutable()):original;
    }
}
