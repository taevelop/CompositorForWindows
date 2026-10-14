using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

public static class MaskTracing
{
    public static DocumentSelection WhitePixels(LayerMask mask,CancellationToken cancellation=default)
    {
        var raster=mask.Pixels;int width=raster.Width,height=raster.Height,stride=width+1;
        bool Selected(int x,int y)=>x>=0&&y>=0&&x<width&&y<height&&raster.Tiles.TryGetValue(new(x/256,y/256),out var tile)&&tile.Bytes[((y%256)*256+x%256)*4]>=128;
        var outgoing=new Dictionary<int,List<int>>();int edgeCount=0;
        void Edge(int x0,int y0,int x1,int y1)
        {
            if(++edgeCount>1_000_000)throw new InvalidOperationException("Subject outline exceeds the selection complexity limit.");
            int start=y0*stride+x0;if(!outgoing.TryGetValue(start,out var ends))outgoing[start]=ends=[];ends.Add(y1*stride+x1);
        }
        for(int y=0;y<height;y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for(int x=0;x<width;x++)if(Selected(x,y))
            {
                if(!Selected(x,y-1))Edge(x,y,x+1,y);
                if(!Selected(x+1,y))Edge(x+1,y,x+1,y+1);
                if(!Selected(x,y+1))Edge(x+1,y+1,x,y+1);
                if(!Selected(x-1,y))Edge(x,y+1,x,y);
            }
        }
        using var builder=new SKPathBuilder();
        while(outgoing.Count!=0)
        {
            cancellation.ThrowIfCancellationRequested();int start=outgoing.Keys.First(),current=start;var loop=new List<int>();
            do
            {
                if(!outgoing.TryGetValue(current,out var ends)||ends.Count==0)throw new InvalidDataException("Subject mask boundary is not closed.");
                int next=ends[^1];ends.RemoveAt(ends.Count-1);if(ends.Count==0)outgoing.Remove(current);
                loop.Add(current);current=next;if(loop.Count%4096==0)cancellation.ThrowIfCancellationRequested();
            }while(current!=start);
            var corners=new List<SKPoint>();
            for(int i=0;i<loop.Count;i++)
            {
                int previous=loop[(i+loop.Count-1)%loop.Count],vertex=loop[i],next=loop[(i+1)%loop.Count];
                if(vertex%stride-previous%stride!=next%stride-vertex%stride||vertex/stride-previous/stride!=next/stride-vertex/stride)corners.Add(new(vertex%stride,vertex/stride));
            }
            if(corners.Count>=3)builder.AddPoly(corners.ToArray(),true);
        }
        using var path=builder.Detach();var selection=new DocumentSelection(path.IsEmpty?"":path.ToSvgPathData());selection.Validate();cancellation.ThrowIfCancellationRequested();return selection;
    }
    public static Document SelectSubject(Document document,LayerMask mask,SelectionMode mode=SelectionMode.Replace,CancellationToken cancellation=default)
    {
        document.Validate();
        if(mask.Pixels.Width!=document.Width||mask.Pixels.Height!=document.Height)throw new ArgumentException("Subject mask must match the canvas.");
        var traced=WhitePixels(mask,cancellation);if(traced.IsEmpty)throw new InvalidOperationException("No foreground subject was detected.");
        var selected=SelectionGeometry.Combine(document.Selection,traced,mode,document.Width,document.Height);cancellation.ThrowIfCancellationRequested();return document with{Selection=selected};
    }
}
