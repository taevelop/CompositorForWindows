using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class ShapeRasterTests
{
    [Fact] public void RectangleEllipseAndRoundedCornersHavePremultipliedCoverage()
    {
        var style=new LayerShapeStyle(ShapeKind.Rectangle,1,0,0);
        Assert.All(ShapeRaster.Create(style,8,8).ToRgba().Chunk(4),pixel=>Assert.Equal(new byte[]{255,0,0,255},pixel));
        foreach(var shape in new[]{style with{CornerRadius=100},style with{Kind=ShapeKind.Ellipse}})
        {
            var bytes=ShapeRaster.Create(shape,16,16).ToRgba();Assert.Equal(0,bytes[3]);Assert.Equal(255,bytes[(8*16+8)*4+3]);
            foreach(var pixel in bytes.Chunk(4)){Assert.Equal(pixel[3],pixel[0]);Assert.Equal(0,pixel[1]);Assert.Equal(0,pixel[2]);}
        }
    }
    [Fact] public void LineUsesFractionalEndpointsAndRoundCapsWhenResized()
    {
        var line=new LayerShapeStyle(ShapeKind.Line,0,0,1,LineWidth:4,Start:new(.25,.5),End:new(.75,.5));
        foreach(int size in new[]{20,40})
        {
            var bytes=ShapeRaster.Create(line,size,10).ToRgba();
            Assert.Equal(255,bytes[(5*size+size/2)*4+3]);Assert.Equal(0,bytes[(5*size)*4+3]);
            Assert.Equal(255,bytes[(5*size+size/4-1)*4+3]);Assert.Equal(0,bytes[(1*size+size/2)*4+3]);
        }
    }
    [Fact] public void RejectsInvalidAndCancelledWorkAndMatchesOriginalTruncatedRasterSize()
    {
        var style=new LayerShapeStyle(ShapeKind.Rectangle,1,1,1);
        Assert.Equal(3,ShapeRaster.Create(style,3.9,2.9).Width);
        Assert.Throws<InvalidDataException>(()=>ShapeRaster.Create(style with{Start=new(double.NaN,0)},3,3));
        Assert.Throws<InvalidDataException>(()=>ShapeRaster.Create(style,30_000,30_000));
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>ShapeRaster.Create(style,3,3,cancellation.Token));
    }
}
