using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;
public sealed class RasterReframeTests
{
    [Fact] public void AlignedExpansionSharesTilesAndSuppliesTransparentPadding()
    {
        var source=LayerMask.Solid(512,256,80).Pixels;
        var result=RasterReframe.Apply(source,new(-256,0,1024,256));
        Assert.Same(source.Tiles[new(0,0)],result.Tiles[new(1,0)]);
        Assert.Same(source.Tiles[new(1,0)],result.Tiles[new(2,0)]);
        Assert.False(result.Tiles.ContainsKey(new(0,0)));Assert.False(result.Tiles.ContainsKey(new(3,0)));
        Assert.Same(source,RasterReframe.Apply(source,new(0,0,512,256)));
    }
    [Fact] public void UnalignedCropAndExpansionKeepSourcePixelsExact()
    {
        var bytes=new byte[300*3*4];for(int y=0;y<3;y++)for(int x=0;x<300;x++){int p=(y*300+x)*4;bytes[p]=(byte)(x%200);bytes[p+1]=(byte)y;bytes[p+3]=255;}
        var source=Raster.FromRgba(300,3,bytes);
        var result=RasterReframe.Apply(source,new(253,-1,8,5)).ToRgba();
        for(int y=0;y<3;y++)for(int x=0;x<8;x++)Assert.Equal(bytes.AsSpan((y*300+253+x)*4,4).ToArray(),result.AsSpan(((y+1)*8+x)*4,4).ToArray());
        Assert.Equal(new byte[32],result.AsSpan(0,32).ToArray());
        Assert.Equal(bytes,source.ToRgba());
    }
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public void RotatedReframeKeepsPixelAndMaskPositions(bool flipX,bool flipY)
    {
        var source=Layer.Blank("Placed",8,4) with{Transform=new(20,30,32,12,37,flipX,flipY),Mask=LayerMask.Solid(1,1,40),Opacity=.4};
        Assert.Same(source,RasterReframe.Apply(source,new(0,0,8,4)));
        var result=RasterReframe.Apply(source,new(-3,-2,16,10));
        foreach(var point in new[]{new PointD(.5,.5),new PointD(7.5,3.5)})
        {
            var before=source.Transform.ToDocument(point,8,4);
            var after=result.Transform.ToDocument(new(point.X+3,point.Y+2),16,10);
            Assert.Equal(before.X,after.X,8);Assert.Equal(before.Y,after.Y,8);
        }
        var mask=result.Mask!.Pixels.ToRgba();
        Assert.Equal(40,mask[(2*16+3)*4]);Assert.Equal(255,mask[0]);Assert.Equal(.4,result.Opacity);
        Assert.Equal(40,source.Mask!.Pixels.ToRgba()[0]);
    }
    [Fact] public void InvalidDimensionsAndCancelledReframeFailBeforeChanges()
    {
        var source=new Raster(8,8);
        Assert.Throws<InvalidDataException>(()=>RasterReframe.Apply(source,new(0,0,30001,1)));
        Assert.Throws<OperationCanceledException>(()=>RasterReframe.Apply(source,new(-1,-1,10,10),cancellation:new CancellationToken(true)));
    }
}
