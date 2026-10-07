using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class ColorBalanceParallelTests
{
    [Theory]
    [InlineData(true,3)]
    [InlineData(false,8)]
    public void PartitionedKernelMatchesOriginalBytes(bool preserve,int workers)
    {
        var random=new Random(842);var source=new byte[(262144+17)*4];
        for(int p=0;p<source.Length;p+=4){byte a=(byte)random.Next(256);source[p+3]=a;for(int c=0;c<3;c++)source[p+c]=(byte)random.Next(a+1);}
        var settings=new ColorBalanceAdjustment(ShadowCyanRed:23,MidCyanRed:-41,HighlightYellowBlue:73,PreserveLuminosity:preserve);
        var expected=(byte[])source.Clone();var actual=(byte[])source.Clone();
        NativePixels.ColorBalance(expected,expected.Length/4,settings.Shadows,settings.Midtones,settings.Highlights,preserve?1:0);
        ColorBalanceProcessor.Apply(actual,settings,workers);Assert.Equal(expected,actual);
    }
}
