using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class AddNoiseTests
{
    private static Raster Gray(int width,int height)
    {
        var pixels=new byte[width*height*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=pixels[i+1]=pixels[i+2]=128;pixels[i+3]=255;}return Raster.FromRgba(width,height,pixels);
    }
    private static uint Hash(uint value)
    {
        unchecked{value^=value>>16;value*=0x7feb352d;value^=value>>15;value*=0x846ca68b;return value^(value>>16);}
    }
    [Fact] public void OriginalUniformHashMatchesFullImageCoordinatesAcrossTilesAndAlpha()
    {
        const int width=520,height=8;var pixels=Gray(width,height).ToRgba();Array.Clear(pixels,0,4);
        for(int y=0;y<height;y++){int p=(y*width+260)*4;pixels[p]=pixels[p+1]=pixels[p+2]=64;pixels[p+3]=128;}
        var source=Raster.FromRgba(width,height,pixels);var result=AddNoise.Apply(source,new(10,Seed:7)).ToRgba();var expected=(byte[])pixels.Clone();
        for(int i=0;i<width*height;i++)
        {
            byte alpha=pixels[i*4+3];if(alpha==0)continue;uint key=Hash(7^Hash((uint)i));
            for(int c=0;c<3;c++)
            {
                uint channelKey=unchecked(key+(uint)c*0x9e3779b9);float unit=(Hash(channelKey)>>8)*(1f/16777216f);
                float value=Math.Clamp(pixels[i*4+c]*255f/alpha+(unit*2-1)*12.75f,0,255);
                expected[i*4+c]=(byte)MathF.Round(value*alpha/255f,MidpointRounding.AwayFromZero);
            }
        }
        Assert.Equal(expected,result);Assert.Equal(pixels,source.ToRgba());
        for(int i=0;i<result.Length;i+=4){Assert.Equal(pixels[i+3],result[i+3]);Assert.True(result[i]<=result[i+3]&&result[i+1]<=result[i+3]&&result[i+2]<=result[i+3]);}
    }
    [Fact] public void GaussianMonochromaticSeedIsStableAndDistributionHasExpectedSpread()
    {
        var source=Gray(1024,32);var settings=new NoiseSettings(10,NoiseDistribution.Gaussian,true,7);
        var result=AddNoise.Apply(source,settings).ToRgba();Assert.Equal(result,AddNoise.Apply(source,settings).ToRgba());
        Assert.False(result.SequenceEqual(AddNoise.Apply(source,settings with{Seed=8}).ToRgba()));
        var values=result.Chunk(4).Select(pixel=>{Assert.Equal(pixel[0],pixel[1]);Assert.Equal(pixel[0],pixel[2]);Assert.Equal(255,pixel[3]);return (double)pixel[0];}).ToArray();
        double mean=values.Average(),deviation=Math.Sqrt(values.Select(v=>(v-mean)*(v-mean)).Average());
        Assert.InRange(mean,126,130);Assert.InRange(deviation,7.5,9.5);Assert.Contains(values,v=>v<115||v>141);
        Assert.Equal(result,AddNoise.Apply(source,settings).ToRgba());
    }
    [Fact] public void SelectionMaskMetadataAndUndoArePreservedAndNoChangeSharesRaster()
    {
        var doc=Document.Create(32,8);var layer=doc.Layers[0] with{Pixels=Gray(32,8),Mask=LayerMask.Solid(32,8),Opacity=.6,Blend=BlendMode.Multiply};doc=doc.Replace(layer) with{Selection=SelectionGeometry.Box(0,0,16,8)};
        var next=AddNoise.Apply(doc,layer.Id,new(30,Seed:7));var edited=next.Layers[0];var before=layer.Pixels.ToRgba();var after=edited.Pixels.ToRgba();
        for(int y=0;y<8;y++)Assert.Equal(before.AsSpan((y*32+16)*4,16*4).ToArray(),after.AsSpan((y*32+16)*4,16*4).ToArray());
        Assert.False(before.SequenceEqual(after));Assert.Same(layer.Mask,edited.Mask);Assert.Equal(layer.Transform,edited.Transform);Assert.Equal(layer.Opacity,edited.Opacity);Assert.Equal(layer.Blend,edited.Blend);
        var session=new EditorSession(doc);session.Apply(_=>next);session.Undo();Assert.Same(doc,session.Document);session.Redo();Assert.Same(next,session.Document);
        Assert.Same(layer.Pixels,AddNoise.Apply(layer.Pixels,new(.1)));Assert.Same(doc,AddNoise.Apply(doc,layer.Id,new(.1)));
        Assert.Throws<ArgumentOutOfRangeException>(()=>AddNoise.Apply(layer.Pixels,new(401)));using var token=new CancellationTokenSource();token.Cancel();Assert.Throws<OperationCanceledException>(()=>AddNoise.Apply(layer.Pixels,new(10),token.Token));
    }
}
