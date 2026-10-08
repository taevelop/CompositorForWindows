using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class ShapeTransformPreviewTests
{
    [Fact] public void KeepsDocumentRadiusAndOriginalPixelsAndMaskImmutable()
    {
        var style=new LayerShapeStyle(ShapeKind.Rectangle,1,0,0,CornerRadius:10);
        var pixels=ShapeRaster.Create(style,40,40);
        var layer=new Layer(Guid.NewGuid(),"Shape",pixels,new(0,0,40,40),Mask:LayerMask.Solid(40,40)){Shape=style};
        var original=Document.Create(200,100) with{Layers=[layer]};
        var changed=original.Replace(layer with{Transform=new(0,0,100,60)});
        var preview=new ShapeTransformPreview();var display=preview.Create(changed,original);
        var shown=display.Layers[0];display.Validate();
        Assert.Equal(ShapeRaster.Create(style,100,60).ToRgba(),shown.Pixels.ToRgba());
        Assert.Same(pixels,changed.Layers[0].Pixels);Assert.Null(layer.Mask!.Placement);
        Assert.Same(layer.Mask.Pixels,shown.Mask!.Pixels);Assert.Equal(shown.Transform,shown.Mask.Placement);
        Assert.Same(display,preview.Create(changed,original));Assert.Same(original,preview.Create(original,original));
    }
    [Fact] public void CapsDisplayGridAndDoesNotRedrawUnchangedOrPixelEditedLayers()
    {
        var style=new LayerShapeStyle(ShapeKind.Rectangle,1,1,1,CornerRadius:100);
        var layer=new Layer(Guid.NewGuid(),"Shape",ShapeRaster.Create(style,40,40),new(0,0,40,40)){Shape=style};
        var original=Document.Create(100,100) with{Layers=[layer]};
        var changed=original.Replace(layer with{Transform=new(0,0,8192,4096)});
        var preview=new ShapeTransformPreview();var shown=preview.Create(changed,original).Layers[0];
        Assert.Equal(2048,shown.Pixels.Width);Assert.Equal(1024,shown.Pixels.Height);
        Assert.Equal(ShapeRaster.Create(style with{CornerRadius=25},2048,1024).ToRgba(),shown.Pixels.ToRgba());
        var edited=changed.Replace(changed.Layers[0] with{Pixels=new Raster(40,40)});
        Assert.Same(edited,preview.Create(edited,original));
        Assert.Same(changed,preview.Create(changed,changed));
    }
}
