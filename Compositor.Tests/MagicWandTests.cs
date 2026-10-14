using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public class MagicWandTests
{
    [Fact]
    public void AlphaToleranceAndNativeHoleOutlineMatchExactPixels()
    {
        var source=Raster.FromRgba(2,1,new byte[]{40,30,20,255,40,30,20,128});
        using(var strict=SelectionGeometry.Path(MagicWand.Select(source,new(0,0),new(0,0,false))))Assert.False(strict.Contains(1.5f,.5f));
        using(var loose=SelectionGeometry.Path(MagicWand.Select(source,new(0,0),new(127,0,false))))Assert.True(loose.Contains(1.5f,.5f));
        var pixels=new byte[6*6*4];
        for(int y=0;y<6;y++)for(int x=0;x<6;x++){int p=(y*6+x)*4;pixels[p]=(byte)(x>=2&&x<=3&&y>=2&&y<=3?200:40);pixels[p+3]=255;}
        using var hole=SelectionGeometry.Path(MagicWand.Select(Raster.FromRgba(6,6,pixels),new(0,0),new(0)));
        for(int y=0;y<6;y++)for(int x=0;x<6;x++)Assert.Equal(!(x>=2&&x<=3&&y>=2&&y<=3),hole.Contains(x+.5f,y+.5f));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ContiguousUsesFourConnectedPixelsAndGlobalMatchesSeparateIsland(bool contiguous)
    {
        const int w=263,h=4;var bytes=new byte[w*h*4];
        for(int y=0;y<h;y++)for(int x=0;x<w;x++){int p=(y*w+x)*4;bytes[p]=(byte)(x<257||x==260?40:200);bytes[p+1]=30;bytes[p+2]=20;bytes[p+3]=255;}
        var source=Raster.FromRgba(w,h,bytes);var result=MagicWand.Select(source,new(255.5,1.5),new(0,0,contiguous));using var path=SelectionGeometry.Path(result);
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)Assert.Equal(x<257||!contiguous&&x==260,path.Contains(x+.5f,y+.5f));Assert.Equal(bytes,source.ToRgba());
    }
    [Fact]
    public void AverageReferenceIncludesAlphaAndSeedMustMatch()
    {
        var bytes=new byte[]{0,0,0,255,100,0,0,255,200,0,0,255};var source=Raster.FromRgba(3,1,bytes);
        var result=MagicWand.Select(source,new(1,0),new(0,1,false));using var path=SelectionGeometry.Path(result);Assert.True(path.Contains(1.5f,.5f));Assert.False(path.Contains(.5f,.5f));
        Assert.True(MagicWand.Select(source,new(0,0),new(0,1,true)).IsEmpty);
        Assert.True(MagicWand.Select(source,new(-1,0),new()).IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(()=>MagicWand.Select(source,new(0,0),new(256)));
        Assert.Throws<OperationCanceledException>(()=>MagicWand.Select(source,new(0,0),new(),new CancellationToken(true)));
    }
}
