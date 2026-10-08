using Compositor.Core;
namespace Compositor.Imaging;

/// <summary>Display-only rounded corners; never publish this document to an editor session.</summary>
public sealed class ShapeTransformPreview
{
    private Document? source, baseline, result;
    private readonly LinkedList<(Key Key,Raster Pixels)> cache=new();
    private readonly long byteLimit;
    private readonly int entryLimit;
    private sealed record Key(LayerShapeStyle Style,int Width,int Height,double Factor);
    public long CachedBytes {get;private set;}
    public ShapeTransformPreview(int entryLimit=4,long byteLimit=64L*1024*1024)
    {
        if(entryLimit<0||byteLimit<0)throw new ArgumentOutOfRangeException();
        this.entryLimit=entryLimit;this.byteLimit=byteLimit;
    }
    public void Clear() { source=null;baseline=null;result=null;cache.Clear();CachedBytes=0; }
    public Document Create(Document document, Document original)
    {
        if(ReferenceEquals(source,document)&&ReferenceEquals(baseline,original))return result!;
        if(!ReferenceEquals(baseline,original))Clear();
        var originals=original.Layers.ToDictionary(l=>l.Id);
        var layers=document.Layers.ToBuilder();bool changed=false;
        for(int i=0;i<layers.Count;i++)
        {
            var layer=layers[i];originals.TryGetValue(layer.Id,out var before);
            if(before is null||before.Transform==layer.Transform||!ReferenceEquals(before.Pixels,layer.Pixels)||
                layer.Shape is not {Kind:ShapeKind.Rectangle,CornerRadius:>0} style)continue;
            var t=layer.Transform;
            if(Math.Abs(t.Width-layer.Pixels.Width)<.5&&Math.Abs(t.Height-layer.Pixels.Height)<.5)continue;
            double factor=Math.Min(1,2048/Math.Max(t.Width,t.Height));
            int width=Math.Max(1,(int)Math.Round(t.Width*factor,MidpointRounding.AwayFromZero));
            int height=Math.Max(1,(int)Math.Round(t.Height*factor,MidpointRounding.AwayFromZero));
            var pixels=GetPixels(new(style,width,height,factor));
            var mask=layer.Mask;
            if(mask is not null&&mask.Placement is null&&(mask.Pixels.Width!=1||mask.Pixels.Height!=1))
                mask=mask with{Placement=t};
            layers[i]=layer with{Pixels=pixels,Shape=style,Mask=mask};changed=true;
        }
        var next=changed?document with{Layers=layers.ToImmutable()}:document;
        source=document;baseline=original;result=next;return next;
    }
    private Raster GetPixels(Key key)
    {
        for(var node=cache.First;node is not null;node=node.Next)
            if(node.Value.Key==key){cache.Remove(node);cache.AddFirst(node);return node.Value.Pixels;}
        var pixels=ShapeRaster.Create(key.Style with{CornerRadius=key.Style.CornerRadius*key.Factor},key.Width,key.Height);
        if(entryLimit==0||pixels.AllocatedBytes>byteLimit)return pixels;
        while(cache.Count>=entryLimit||CachedBytes+pixels.AllocatedBytes>byteLimit)
        {CachedBytes-=cache.Last!.Value.Pixels.AllocatedBytes;cache.RemoveLast();}
        cache.AddFirst((key,pixels));CachedBytes+=pixels.AllocatedBytes;return pixels;
    }
}
