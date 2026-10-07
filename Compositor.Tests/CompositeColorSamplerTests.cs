using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Xunit;
namespace Compositor.Tests;
public sealed class CompositeColorSamplerTests
{
    private static Document Pixels(byte[] bytes)
    {
        var d=Document.Create(bytes.Length/4,1);
        return d.Replace(d.Layers[0] with{Pixels=Raster.FromRgba(d.Width,1,bytes),Transform=new(0,0,d.Width,1,Sampling:Sampling.Nearest)});
    }
    [Fact] public void FloorCoordinatesAndUnpremultiplyWithoutCheckerboard()
    {
        using var sampler=new CompositeColorSampler();var d=Pixels([64,32,16,128,0,255,0,255,0,0,0,0]);
        Assert.Equal(new SampledColor(128,64,32),sampler.Sample(d,new(.99,.99)));
        Assert.Equal(new SampledColor(0,255,0),sampler.Sample(d,new(1,0)));
        Assert.Null(sampler.Sample(d,new(2,0)));Assert.Null(sampler.Sample(d,new(-.01,0)));
        Assert.Null(sampler.Sample(d,new(3,0)));Assert.Null(sampler.Sample(d,new(0,1)));
        Assert.Null(sampler.Sample(d,new(double.NaN,0)));Assert.Null(sampler.Sample(d,new(0,double.PositiveInfinity)));
    }
    [Fact] public void MatchesCompositeWithGroupOpacityMaskAndHiddenLayers()
    {
        var d=Pixels([255,0,0,255,0,255,0,255]);var group=Layer.Group("Group",2,1) with{Opacity=.5};
        var top=d.Layers[0] with{Id=Guid.NewGuid(),Pixels=Raster.FromRgba(2,1,[0,0,255,255,0,0,255,255]),ParentId=group.Id,Mask=LayerMask.Solid(2,1,128)};
        var hidden=top with{Id=Guid.NewGuid(),ParentId=null,Visible=false};d=d with{Layers=d.Layers.Add(group).Add(top).Add(hidden)};
        using var renderer=new CanvasRenderer();using var bitmap=new SKBitmap(CanvasRenderer.Info(2,1));
        using(var canvas=new SKCanvas(bitmap)){canvas.Clear();renderer.Draw(canvas,d);canvas.Flush();}
        var pixels=bitmap.Bytes;using var sampler=new CompositeColorSampler();
        for(int x=0;x<2;x++)Assert.Equal(new SampledColor(pixels[x*4],pixels[x*4+1],pixels[x*4+2]),sampler.Sample(d,new(x,0)));
    }
    [Fact] public void AdjustmentCacheTracksNewImmutableDocuments()
    {
        var d=Pixels([255,0,0,255]);d=d with{Layers=d.Layers.Add(Layer.InvertLayer(1,1))};
        using var sampler=new CompositeColorSampler();Assert.Equal(new SampledColor(0,255,255),sampler.Sample(d,new(0,0)));
        var changed=d.Replace(d.Layers[0] with{Pixels=Raster.FromRgba(1,1,[0,0,255,255])});
        Assert.Equal(new SampledColor(255,255,0),sampler.Sample(changed,new(0,0)));
        Assert.Equal(new SampledColor(0,255,255),sampler.Sample(d,new(0,0)));
    }
    [Fact] public void DisposeRejectsFurtherSampling()
    {
        var sampler=new CompositeColorSampler();sampler.Dispose();sampler.Dispose();
        Assert.Throws<ObjectDisposedException>(()=>sampler.Sample(Document.Create(1,1),new(0,0)));
    }
}
