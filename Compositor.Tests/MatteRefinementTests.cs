using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;
public class MatteRefinementTests
{
    [Theory]
    [InlineData(-2,0)]
    [InlineData(2,255)]
    public void ShiftEdgeUsesOriginalContractionAndExpansionThresholds(double shift,int expected)
    {
        var source=LayerMask.Solid(17,9,128);
        var result=MatteRefinement.Apply(source,new Raster(17,9),new(true,0,0,shift));
        Assert.Equal(expected,result.Pixels.ToRgba()[0]);
        Assert.Equal(128,source.Pixels.ToRgba()[0]);
    }

    [Fact]
    public void BasicSharesMatteAndAdvancedContrastKeepsGrayscaleAndPadding()
    {
        var mask=LayerMask.Solid(17,9,192);var guide=new Raster(17,9);
        Assert.Same(mask,MatteRefinement.Apply(mask,guide,new()));
        var after=MatteRefinement.Apply(mask,guide,new(true,0,50,0));
        double slope=1/(1-.5*.98);byte expected=(byte)Math.Clamp((192/255d-.5)*slope*255+127.5+.5,0,255);
        var pixels=after.Pixels.ToRgba();Assert.Equal(expected,pixels[0]);
        for(int i=0;i<pixels.Length;i+=4){Assert.Equal(pixels[i],pixels[i+1]);Assert.Equal(pixels[i],pixels[i+2]);Assert.Equal(255,pixels[i+3]);}
        Assert.Equal(192,mask.Pixels.ToRgba()[0]);
        Assert.Throws<ArgumentOutOfRangeException>(()=>MatteRefinement.Apply(mask,guide,new(true,41)));
        Assert.Throws<OperationCanceledException>(()=>MatteRefinement.Apply(mask,guide,new(),cancellation:new CancellationToken(true)));
    }
}
