using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class LayerFillTests
{
    [Fact] public void ImageFillExpandsToCanvasAndKeepsOriginalImmutable()
    {
        var doc=Document.Create(4,2);var source=doc.Layers[0] with{Pixels=Raster.FromRgba(1,1,[20,30,40,128]),Transform=new(2,1,1,1)};doc=doc.Replace(source);
        var result=LayerFill.Apply(doc,source.Id,10,20,30);var layer=result.Layers[0];
        Assert.Equal(4,layer.Pixels.Width);Assert.Equal(2,layer.Pixels.Height);Assert.Equal(0,layer.Transform.X);
        Assert.Equal(new byte[]{10,20,30,255},layer.Pixels.ToRgba()[..4]);Assert.Equal(new byte[]{20,30,40,128},source.Pixels.ToRgba());
        Assert.Same(result,LayerFill.Apply(result,source.Id,10,20,30));
        var session=new EditorSession(doc);session.Apply(_=>result);session.Undo();Assert.Same(doc,session.Document);session.Redo();Assert.Same(result,session.Document);
    }
    [Fact] public void MaskFillUsesGrayWithoutChangingImageOrTransform()
    {
        var doc=Document.Create(2,1);var source=doc.Layers[0] with{Mask=LayerMask.Solid(1,1,255)};doc=doc.Replace(source);
        var result=LayerFill.Apply(doc,source.Id,64,128,255,true);var layer=result.Layers[0];
        Assert.Same(source.Pixels,layer.Pixels);Assert.Equal(source.Transform,layer.Transform);
        Assert.Equal(new byte[]{64,64,64,255},layer.Mask!.Pixels.ToRgba()[..4]);
    }
    [Fact] public void SelectionAndEmptySelectionRestrictFill()
    {
        var doc=Document.Create(3,1);doc=doc with{Selection=SelectionGeometry.Box(1,0,1,1)};
        var result=LayerFill.Apply(doc,doc.Layers[0].Id,90,80,70);var pixels=result.Layers[0].Pixels.ToRgba();
        Assert.Equal(0,pixels[3]);Assert.Equal(255,pixels[7]);Assert.Equal(0,pixels[11]);
        var empty=doc with{Selection=DocumentSelection.Empty};Assert.Same(empty,LayerFill.Apply(empty,empty.Layers[0].Id,90,80,70));
    }
    [Fact] public void CancellationPreservesSource()
    {
        var doc=Document.Create(2,1);using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>LayerFill.Apply(doc,doc.Layers[0].Id,1,2,3,cancellation:cancellation.Token));
        Assert.Empty(doc.Layers[0].Pixels.Tiles);
    }
}
