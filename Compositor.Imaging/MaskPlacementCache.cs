using Compositor.Core;

namespace Compositor.Imaging;

/// <summary>Renderer-owned LRU; callers must serialize access on its owner thread.</summary>
public sealed class MaskPlacementCache
{
    private sealed record Key(Raster Source,LayerTransform Placement,LayerTransform Layer,int Width,int Height);
    private sealed record Entry(Key Key,LayerMask Mask,long Bytes);
    private readonly Dictionary<Key,LinkedListNode<Entry>> entries=[];
    private readonly LinkedList<Entry> recent=[];
    private readonly long maxBytes;
    private readonly int maxEntries;
    public long RetainedBytes {get;private set;}
    public int Count=>entries.Count;
    public int BuildCount {get;private set;}
    public MaskPlacementCache(long maxBytes=64L*1024*1024,int maxEntries=8)
    {
        if(maxBytes<0||maxEntries<0)throw new ArgumentOutOfRangeException(nameof(maxBytes));
        this.maxBytes=maxBytes;this.maxEntries=maxEntries;
    }
    public LayerMask Resolve(LayerMask mask,LayerTransform placement,LayerTransform layer,int width,int height,
        CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        placement.Validate();layer.Validate();Limits.CheckDimensions(width,height);
        if(mask.Pixels.Width==1&&mask.Pixels.Height==1)return mask;
        var key=new Key(mask.Pixels,placement,layer,width,height);
        if(entries.TryGetValue(key,out var cached))
        {
            recent.Remove(cached);recent.AddLast(cached);
            return cached.Value.Mask with { Enabled=mask.Enabled };
        }
        var result=MaskPlacement.Resolve(mask,placement,layer,width,height,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();BuildCount++;
        // Include retained source grids, and conservatively count shared tiles per grid.
        long bytes=checked(mask.Pixels.AllocatedBytes+result.Pixels.AllocatedBytes);
        if(bytes>maxBytes||maxEntries==0)return result;
        while(entries.Count>=maxEntries||RetainedBytes>maxBytes-bytes)
        {
            var oldest=recent.First!;recent.RemoveFirst();entries.Remove(oldest.Value.Key);
            RetainedBytes-=oldest.Value.Bytes;
        }
        entries.Add(key,recent.AddLast(new Entry(key,result,bytes)));RetainedBytes+=bytes;
        return result;
    }
    public void Clear(){entries.Clear();recent.Clear();RetainedBytes=0;}
}
