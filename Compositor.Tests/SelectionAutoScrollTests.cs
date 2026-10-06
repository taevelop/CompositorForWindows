using Compositor.Core;
using Xunit;
namespace Compositor.Tests;
public sealed class SelectionAutoScrollTests
{
    [Theory][InlineData(50,50,0,0)][InlineData(12,12,0,0)][InlineData(10,50,2.8,0)][InlineData(90,50,-2.8,0)][InlineData(50,10,0,2.8)][InlineData(50,90,0,-2.8)][InlineData(-100,200,40,-40)]
    public void EdgesRampAndCapIndependently(double x,double y,double dx,double dy)
    {Assert.Equal(new PointD(dx,dy),SelectionAutoScroll.Delta(new(x,y),100,100));}
    [Fact] public void InvalidViewportAndCoordinatesDoNotScroll()
    {
        Assert.Equal(new PointD(0,0),SelectionAutoScroll.Delta(new(double.NaN,0),100,100));
        Assert.Equal(new PointD(0,0),SelectionAutoScroll.Delta(new(0,0),0,100));
    }
}
