using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;
public class SubjectMaskTests
{
    [Fact]
    public void SelectionCombinesExistingMaskAndPreservesImagePixelsAndUndo()
    {
        var doc=Document.Create(16,8);var layer=doc.Layers[0] with{Mask=LayerMask.Solid(16,8,128),Pixels=ShapeRaster.Create(new(ShapeKind.Rectangle,.2,.4,.6),16,8)};
        doc=doc.Replace(layer) with{Selection=SelectionGeometry.Box(0,0,8,8,antialiased:false)};
        var result=SubjectMask.Apply(doc,layer.Id,LayerMask.Solid(16,8,128));
        Assert.Same(layer.Pixels,result.Layers[0].Pixels);
        var pixels=result.Layers[0].Mask!.Pixels.ToRgba();
        for(int y=0;y<8;y++)for(int x=0;x<16;x++)Assert.Equal(x<8?(byte)64:(byte)128,pixels[(y*16+x)*4]);
        var session=new EditorSession(doc);session.Begin();session.Preview(result);session.Commit();session.Undo();Assert.Same(doc,session.Document);
        Assert.Throws<ArgumentException>(()=>SubjectMask.Apply(doc,layer.Id,LayerMask.Solid(1,1)));
        Assert.Throws<OperationCanceledException>(()=>SubjectMask.Apply(doc,layer.Id,LayerMask.Solid(16,8),new CancellationToken(true)));
    }
}
