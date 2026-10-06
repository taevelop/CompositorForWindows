using System.Collections.Immutable;
using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;

public sealed class HueSaturationTests
{
    private static HueSaturationAdjustment Settings(HueRange range, HueRangeAdjustment adjustment) =>
        new() { Range = range, Adjustments = ImmutableDictionary<HueRange,HueRangeAdjustment>.Empty.Add(range,adjustment) };

    [Fact] public void BandsWrapAndFallOffLikeOriginal()
    {
        var b=HueBand.Default(HueRange.Reds);
        foreach(double h in new[]{0d,345,15}) Assert.Equal(1,b.Weight(h));
        Assert.Equal(.5,b.Weight(330));Assert.Equal(.5,b.Weight(30));
        foreach(double h in new[]{315d,45,180}) Assert.Equal(0,b.Weight(h));
        Assert.Equal(1,HueBand.Default(HueRange.Master).Weight(123));
        b=HueBand.Default(HueRange.Greens);
        Assert.Same(b,b.MoveHandle(1,200));Assert.Equal(110,b.MoveHandle(1,110).RangeStart);
    }
    [Fact] public void SamplingCentersIncludesAndExcludes()
    {
        var b=HueBand.Default(HueRange.Greens).Centered(240);
        Assert.Equal(HueBand.Default(HueRange.Blues),b);
        var wider=b.Include(180);Assert.Equal(1,wider.Weight(180));
        Assert.Equal(0,wider.Exclude(180).Weight(180));
        Assert.Equal(b,b.Include(240));Assert.Equal(b,b.Exclude(100));
    }
    [Fact] public void DefaultBypassIsByteExactForAllAlpha()
    {
        var bytes=new byte[256*4];for(int a=0;a<256;a++){bytes[a*4]=(byte)(a/2);bytes[a*4+1]=(byte)a;bytes[a*4+3]=(byte)a;}
        var before=bytes.ToArray();HueSaturationProcessor.Apply(bytes,new());Assert.Equal(before,bytes);
    }
    [Fact] public void MasterRotatesPrimaryAndKeepsNeutralGray()
    {
        byte[] pixels=[255,0,0,255,128,128,128,255,0,0,128,128];
        HueSaturationProcessor.Apply(pixels,Settings(HueRange.Master,new(Hue:120)));
        Assert.Equal(new byte[]{0,255,0,255,128,128,128,255,128,0,0,128},pixels);
    }
    [Fact] public void IndependentRangesCombineAndInverseOnlyAffectsSelectedRange()
    {
        var settings=Settings(HueRange.Reds,new(Hue:60));
        settings=settings with {Adjustments=settings.Adjustments.Add(HueRange.Blues,new(Saturation:-100))};
        byte[] p=[255,0,0,255,0,0,255,255];
        HueSaturationProcessor.Apply(p,settings);
        Assert.Equal(new byte[]{255,255,0,255,128,128,128,255},p);
        settings=Settings(HueRange.Reds,new(Lightness:-100)) with {InvertRange=true};
        byte[] inverted=[255,0,0,255,0,0,255,255];
        HueSaturationProcessor.Apply(inverted,settings);
        Assert.Equal(new byte[]{255,0,0,255,0,0,0,255},inverted);
    }
    [Theory][InlineData(-100,0)][InlineData(100,255)]
    public void LightnessReachesBlackAndWhite(double lightness,byte target)
    {
        byte[] p=[80,180,30,255];HueSaturationProcessor.Apply(p,Settings(HueRange.Master,new(Lightness:lightness)));
        Assert.Equal(new byte[]{target,target,target,255},p);
    }
    [Fact] public void ColorizeUsesSelectedRangeAndPreservesEveryAlpha()
    {
        var settings=Settings(HueRange.Greens,new(Hue:240,Saturation:100)) with {Colorize=true};
        var pixels=new byte[256*4];for(int a=0;a<256;a++){pixels[a*4]=(byte)a;pixels[a*4+3]=(byte)a;}
        HueSaturationProcessor.Apply(pixels,settings);
        for(int a=0;a<256;a++)Assert.Equal(new byte[]{0,0,(byte)a,(byte)a},pixels.AsSpan(a*4,4).ToArray());
    }
    [Fact] public void CpuCubeInterpolatesEightCornersAndPremultipliesOnce()
    {
        var cube=new float[33*33*33*4];
        for(int b=0;b<33;b++)for(int g=0;g<33;g++)for(int r=0;r<33;r++)
        {
            int p=((b*33+g)*33+r)*4;
            cube[p]=r*r/1024f;cube[p+1]=g*g/1024f;cube[p+2]=b*b/1024f;cube[p+3]=1;
        }
        byte[] pixels=[17,41,103,128];NativePixels.HueCube(pixels,1,cube);
        byte Expected(int v){double x=v*32d/128,lo=Math.Floor(x),t=x-lo;return (byte)Math.Round(((1-t)*lo*lo+t*(lo+1)*(lo+1))*128/1024,MidpointRounding.AwayFromZero);}
        Assert.Equal(new byte[]{Expected(17),Expected(41),Expected(103),128},pixels);
    }
    [Fact] public void SettingsEqualityIsValueBasedAndEditsAreImmutable()
    {
        var s=Settings(HueRange.Reds,new(Hue:10));var same=Settings(HueRange.Reds,new(Hue:10));
        Assert.Equal(s,same);Assert.Equal(s.GetHashCode(),same.GetHashCode());
        var edited=s with {Adjustments=s.Adjustments.SetItem(HueRange.Reds,new(Hue:50))};
        Assert.Equal(10,s.Adjustment(HueRange.Reds).Hue);Assert.NotEqual(s,edited);
    }
    [Theory][InlineData(-361)][InlineData(361)][InlineData(double.NaN)][InlineData(double.PositiveInfinity)]
    public void InvalidHueRejected(double hue)=>Assert.Throws<InvalidDataException>(()=>Settings(HueRange.Master,new(Hue:hue)).Validate());
    [Fact] public void InvalidBandsRangesAndPixelLengthsRejected()
    {
        Assert.Throws<InvalidDataException>(()=>new HueBand(double.NaN,0,0,0).Validate());
        Assert.Throws<InvalidDataException>(()=>(new HueSaturationAdjustment{Range=(HueRange)99}).Validate());
        Assert.Throws<ArgumentException>(()=>HueSaturationProcessor.Apply(new byte[3],new()));
    }
}
