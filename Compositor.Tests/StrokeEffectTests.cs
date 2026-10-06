using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;
public sealed class StrokeEffectTests
{
    [Theory][InlineData(false,1)][InlineData(true,1)][InlineData(false,4)][InlineData(true,4)][InlineData(false,500)][InlineData(true,500)]
    public void SlidingWindowMatchesIndependentSquareReference(bool inside, int radius)
    {
        const int w=19,h=13; var source=new byte[w*h]; new Random(38).NextBytes(source);
        var result=StrokeProcessor.Coverage(source,w,h,radius,inside);
        for(int y=0;y<h;y++) for(int x=0;x<w;x++)
        {
            int extreme=inside?255:0;
            // Outside image coverage is zero; a radius beyond the image is handled without a huge reference loop.
            if(inside && (x-radius<0 || y-radius<0 || x+radius>=w || y+radius>=h)) extreme=0;
            else for(int yy=Math.Max(0,y-radius);yy<=Math.Min(h-1,y+radius);yy++)
                 for(int xx=Math.Max(0,x-radius);xx<=Math.Min(w-1,x+radius);xx++)
                    extreme=inside?Math.Min(extreme,source[yy*w+xx]):Math.Max(extreme,source[yy*w+xx]);
            Assert.Equal((byte)Math.Max(0,inside?source[y*w+x]-extreme:extreme-source[y*w+x]),result[y*w+x]);
        }
    }
    private static Document Sample(bool inside=false)
    {
        var d=Document.Create(40,40); byte[] bytes=new byte[10*10*4];
        for(int p=0;p<bytes.Length;p+=4) { bytes[p]=255;bytes[p+3]=255; }
        return d.Replace(d.Layers[0] with { Pixels=Raster.FromRgba(10,10,bytes),Transform=new(15,15,10,10),
            Effects=new(Stroke:new(2,0,1,0,1,inside)) });
    }
    private static byte[] Pixels(SKImage image) { using var b=new SKBitmap(CanvasRenderer.Info(image.Width,image.Height)); Assert.True(image.ReadPixels(b.Info,b.GetPixels(),b.RowBytes,0,0));return b.GetPixelSpan().ToArray(); }
    private static byte[] Render(Document d) { using var r=new CanvasRenderer();using var i=r.Flatten(d);return Pixels(i); }
    [Theory][InlineData(false)][InlineData(true)]
    public void RingPositionAndOpacityAreCorrect(bool inside)
    {
        var d=Sample(inside); var actual=Render(d);
        int edge=(inside?15:13)*40+20;
        Assert.Equal(255,actual[edge*4+1]);Assert.Equal(255,actual[edge*4+3]);
        Assert.Equal(255,actual[(20*40+20)*4]);
        Assert.Equal(0,actual[(12*40+20)*4+3]);
        if(inside) Assert.Equal(0,actual[(14*40+20)*4+3]);
        var half=Render(d.Replace(d.Layers[0] with {Opacity=.5}));
        Assert.InRange(half[edge*4+3],(byte)127,(byte)128);
    }
    [Fact] public void MaskAndCacheChangesMatchFreshViewportAndDisabledEqualsOriginal()
    {
        var d=Sample(); var l=d.Layers[0];using var viewport=new ViewportRenderer();
        foreach(var changed in new[] { d, d.Replace(l with {Effects=l.Effects! with {Stroke=new(4,0,0,1,1,true)}}),
            d.Replace(l with {Mask=LayerMask.Solid(10,10,128),Transform=l.Transform with {Rotation=35}}),
            d.Replace(l with {Effects=l.Effects! with {Stroke=l.Effects.Stroke! with {Enabled=false}}}),d })
        {
            using var image=viewport.Render(changed,40,40,1,0,0);
            Assert.Equal(Render(changed),Pixels(image));
        }
        Assert.Equal(Render(d.Replace(l with {Effects=null})),Render(d.Replace(l with {Effects=l.Effects! with {Stroke=new(0)}})));
        Assert.All(Render(d.Replace(l with {Mask=LayerMask.Solid(1,1,0)})),b=>Assert.Equal((byte)0,b));
    }
    [Fact] public void AllThreeEffectsRoundTripWithoutChangingPixels()
    {
        var d=Sample(true);var l=d.Layers[0];
        d=d.Replace(l with {Effects=l.Effects! with {Shadow=new(),ColorOverlay=new(.2,.4,.6,.3)}});
        string root=Path.Combine(Path.GetTempPath(),"CompositorStroke-"+Guid.NewGuid().ToString("N")+".comp");
        try
        {
            ProjectStore.Save(d,l.Id,root);var loaded=ProjectStore.Load(root).Document;
            Assert.Equal(d.Layers[0].Effects,loaded.Layers[0].Effects);
            Assert.Equal(l.Pixels.ToRgba(),loaded.Layers[0].Pixels.ToRgba());Assert.Equal(Render(d),Render(loaded));
            var s=new EditorSession(loaded);s.Apply(x=>x.Replace(x.Layers[0] with {Effects=x.Layers[0].Effects! with {Stroke=null}}));
            Assert.Equal(0,s.HistoryRetainedBytes);s.Undo();Assert.Equal(d.Layers[0].Effects,s.ActiveLayer!.Effects);s.Redo();Assert.Null(s.ActiveLayer!.Effects!.Stroke);
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Theory][InlineData(-1)][InlineData(501)][InlineData(double.NaN)]
    public void InvalidSizeIsRejected(double size)=>Assert.Throws<InvalidDataException>(()=>new StrokeEffect(size).Validate());
}
