using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class MaskPlacementTests
{
    [Fact] public void CacheReusesImmutableCoverageAndHonorsEnabledChangesCancellationAndLruBudget()
    {
        var mask=LayerMask.Solid(2,2,128);var t=new LayerTransform(0,0,2,2);
        var cache=new MaskPlacementCache(4L*PixelTile.ByteCount,2);
        var first=cache.Resolve(mask,t,t,2,2);
        var disabled=cache.Resolve(mask with{Enabled=false},t,t,2,2);
        Assert.Same(first.Pixels,disabled.Pixels);Assert.False(disabled.Enabled);Assert.Equal(1,cache.BuildCount);
        var second=cache.Resolve(mask,t with{X=1},t,2,2);
        cache.Resolve(mask,t,t,2,2); // touch first, so the shifted grid is evicted next.
        cache.Resolve(mask,t with{Y=1},t,2,2);
        Assert.Equal(2,cache.Count);Assert.True(cache.RetainedBytes<=4L*PixelTile.ByteCount);
        Assert.Same(first.Pixels,cache.Resolve(mask,t,t,2,2).Pixels);
        Assert.NotSame(second.Pixels,cache.Resolve(mask,t with{X=1},t,2,2).Pixels);
        using var cancel=new CancellationTokenSource();cancel.Cancel();int builds=cache.BuildCount;
        Assert.Throws<OperationCanceledException>(()=>cache.Resolve(mask,t with{X=1},t,2,2,cancel.Token));
        Assert.Equal(builds,cache.BuildCount);
        cache.Clear();Assert.Equal(0,cache.Count);Assert.Equal(0,cache.RetainedBytes);
        var tiny=new MaskPlacementCache(1);tiny.Resolve(mask,t,t,2,2);Assert.Equal(0,tiny.Count);
    }
    [Fact] public void RendererOwnsPlacementCacheAndSourcePixelReplacementInvalidatesIt()
    {
        using var renderer=new CanvasRenderer();var t=new LayerTransform(0,0,2,2);
        var mask=LayerMask.Solid(2,2,128);var first=renderer.ResolvePlacedMask(mask,t,t,2,2);
        Assert.Same(first.Pixels,renderer.ResolvePlacedMask(mask,t,t,2,2).Pixels);
        var changed=renderer.ResolvePlacedMask(LayerMask.Solid(2,2,255),t,t,2,2);
        Assert.NotSame(first.Pixels,changed.Pixels);Assert.Equal(255,changed.Pixels.ToRgba()[0]);
        Assert.Equal(128,first.Pixels.ToRgba()[0]);
    }
    [Fact] public void LinkedMovementFollowsLayerAndUnlinkedMovementKeepsDocumentPlacement()
    {
        var initial=new LayerTransform(10,20,8,8);var to=initial with{X=30,Width=16};
        Assert.Null(MaskPlacement.Move(null,true,initial,to));
        Assert.Equal(initial,MaskPlacement.Move(null,false,initial,to));
        var separate=initial with{X=12,Y=24,Width=4,Height=4};
        Assert.Equal(GroupTransform.Following(separate,initial,to),MaskPlacement.Move(separate,true,initial,to));
        Assert.Equal(separate,MaskPlacement.Move(separate,false,initial,to));
    }
    [Fact] public void ResolvePreservesSourceAndMapsRotationFlipAndDifferentGrids()
    {
        var bytes=new byte[]{0,0,0,255,255,255,255,255,0,0,0,255,255,255,255,255};
        var seed=LayerMask.Solid(2,2);var tile=seed.Pixels.Tiles[new(0,0)].Bytes.ToArray(); bytes.AsSpan(0,8).CopyTo(tile);bytes.AsSpan(8,8).CopyTo(tile.AsSpan(PixelTile.Stride));
        var mask=seed.WithPixels(new Raster(2,2,seed.Pixels.Tiles.SetItem(new(0,0),new PixelTile(tile))));
        var placement=new LayerTransform(0,0,2,2);
        Assert.Equal(bytes,MaskPlacement.Resolve(mask,placement,placement,2,2).Pixels.ToRgba());
        var flipped=MaskPlacement.Resolve(mask,placement with{FlipX=true},placement,2,2).Pixels.ToRgba();
        Assert.Equal(255,flipped[0]);Assert.Equal(0,flipped[4]);
        var rotated=MaskPlacement.Resolve(mask,placement with{Rotation=180},placement,2,2).Pixels.ToRgba();
        Assert.Equal(flipped,rotated);Assert.Equal(bytes,mask.Pixels.ToRgba());
        Assert.Equal(257,MaskPlacement.Resolve(mask,placement,new(0,0,2,2),257,3).Pixels.Width);
    }
    [Fact] public void BackgroundUsesMaskEdgesAndUniformMasksRemainShared()
    {
        var layer=new LayerTransform(0,0,4,4);var outside=layer with{X=20};
        foreach(byte gray in new byte[]{0,255})
        {
            var mask=LayerMask.Solid(2,2,gray);
            Assert.All(MaskPlacement.Resolve(mask,outside,layer,4,4).Pixels.ToRgba().Chunk(4),p=>Assert.Equal(gray,p[0]));
        }
        var uniform=LayerMask.Solid(1,1,128);Assert.Same(uniform,MaskPlacement.Resolve(uniform,outside,layer,4,4));
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        Assert.Throws<OperationCanceledException>(()=>MaskPlacement.Resolve(uniform,outside,layer,4,4,cancel.Token));
    }
}
