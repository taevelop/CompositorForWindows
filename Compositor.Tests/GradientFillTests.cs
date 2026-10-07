using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class GradientFillTests
{
    [Fact] public void LinearEndpointsMidpointAndReverseUseDocumentCoordinates()
    {
        var doc=Document.Create(3,1);var settings=new GradientFillSettings(new(.5,.5),new(2.5,.5),255,0,0,0,0,255,Style:GradientStyle.ForegroundToBackground);
        var result=GradientFill.Apply(doc,doc.Layers[0].Id,settings).Layers[0].Pixels.ToRgba();
        Assert.Equal(new byte[]{255,0,0,255,128,0,128,255,0,0,255,255},result);
        var reversed=GradientFill.Apply(doc,doc.Layers[0].Id,settings with{Reversed=true}).Layers[0].Pixels.ToRgba();
        Assert.Equal(new byte[]{0,0,255,255},reversed[..4]);Assert.Empty(doc.Layers[0].Pixels.Tiles);
    }
    [Fact] public void TransparentGradientCompositesOverOriginalAndMasksRemainOpaque()
    {
        var doc=Document.Create(3,1);var layer=doc.Layers[0] with{Pixels=Raster.FromRgba(3,1,[0,0,255,255,0,0,255,255,0,0,255,255]),Mask=LayerMask.Solid(3,1,128)};doc=doc.Replace(layer);
        var settings=new GradientFillSettings(new(.5,.5),new(2.5,.5),255,0,0,0,0,0);
        var result=GradientFill.Apply(doc,layer.Id,settings).Layers[0].Pixels.ToRgba();Assert.Equal(new byte[]{128,0,128,255},result[4..8]);Assert.Equal(new byte[]{0,0,255,255},result[8..]);
        var mask=GradientFill.Apply(doc,layer.Id,settings,true).Layers[0].Mask!.Pixels.ToRgba();
        Assert.Equal(new byte[]{255,255,255,255,192,192,192,255,128,128,128,255},mask);
    }
    [Fact] public void RadialExtendsEndpointAndShortLineDoesNothing()
    {
        var doc=Document.Create(5,1);var settings=new GradientFillSettings(new(2.5,.5),new(3.5,.5),255,255,255,0,0,0,GradientShape.Radial,GradientStyle.ForegroundToBackground);
        var result=GradientFill.Apply(doc,doc.Layers[0].Id,settings).Layers[0].Pixels.ToRgba();
        Assert.Equal(255,result[8]);Assert.Equal(0,result[0]);Assert.Equal(0,result[16]);
        Assert.Same(doc,GradientFill.Apply(doc,doc.Layers[0].Id,settings with{End=new(2.6,.5)}));
        Assert.Throws<ArgumentException>(()=>GradientFill.Apply(doc,doc.Layers[0].Id,settings with{Opacity=double.NaN}));
    }
}
