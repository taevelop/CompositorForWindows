using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;
public class GuidedMatteTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(250)]
    public void RunningMeanMatchesIndependentSquareIncludingClampedEdges(int radius)
    {
        const int w=7,h=5;var source=Enumerable.Range(0,w*h).Select(i=>(i%11)/10f).ToArray();var result=GuidedMatte.Box(source,w,h,radius);
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            double sum=0;for(int dy=-radius;dy<=radius;dy++)for(int dx=-radius;dx<=radius;dx++)sum+=source[Math.Clamp(y+dy,0,h-1)*w+Math.Clamp(x+dx,0,w-1)];
            Assert.InRange(Math.Abs(result[y*w+x]-sum/((radius*2d+1)*(radius*2d+1))),0,1e-5);
        }
    }
    [Fact]
    public void ConstantMatteIsPreservedAndGuideEdgesRemainSharp()
    {
        var guide=Enumerable.Range(0,64).Select(i=>i%16<8?0f:1f).ToArray();var mask=Enumerable.Repeat(.4f,64).ToArray();
        foreach(float value in GuidedMatte.Filter(mask,guide,16,4,3))Assert.InRange(value,.3999f,.4001f);
        var result=GuidedMatte.Filter(guide,guide,16,4,3);Assert.InRange(result[7],0,.001f);Assert.InRange(result[8],.999f,1);
        Assert.Throws<ArgumentException>(()=>GuidedMatte.Filter(mask,guide,16,4,0));
        Assert.Throws<OperationCanceledException>(()=>GuidedMatte.Filter(mask,guide,16,4,3,new CancellationToken(true)));
    }
}
