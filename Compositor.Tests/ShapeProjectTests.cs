using Compositor.Core;
using Compositor.Imaging;
using System.Text.Json.Nodes;
using Xunit;

namespace Compositor.Tests;

public sealed class ShapeProjectTests
{
    [Fact] public void TransformRedrawKeepsRadiusStyleAndOneUndoAndCancelRestoresOriginal()
    {
        var d=Document.Create(32,32);var style=new LayerShapeStyle(ShapeKind.Rectangle,1,0,0,2);
        var layer=d.Layers[0] with {Pixels=ShapeRaster.Create(style,8,8),Shape=style,Transform=new(0,0,8,8)};
        d=d.Replace(layer);var session=new EditorSession(d);
        using(var edit=LayerTransformEdit.Begin(session))
        {edit.Preview(layer.Transform with {Width=16,Height=16});edit.Complete();}
        var result=session.Document.Layers[0];Assert.Equal(16,result.Pixels.Width);Assert.Equal(style,result.Shape);
        Assert.Equal(ShapeRaster.Create(style,16,16).ToRgba(),result.Pixels.ToRgba());
        session.Undo();Assert.Same(layer.Pixels,session.Document.Layers[0].Pixels);Assert.False(session.CanUndo);
        session.Redo();Assert.Equal(16,session.Document.Layers[0].Pixels.Width);
        using(var edit=LayerTransformEdit.Begin(session))edit.Preview(result.Transform with {Width=24});
        Assert.Same(result.Pixels,session.Document.Layers[0].Pixels);
    }
    [Fact] public void MaskedResizeFailureRestoresWholeTransactionAndUniformMaskIsPreserved()
    {
        var d=Document.Create(16,16);var style=new LayerShapeStyle(ShapeKind.Ellipse,0,1,0);
        var layer=d.Layers[0] with {Pixels=ShapeRaster.Create(style,16,16),Shape=style,Mask=LayerMask.Solid(16,16,128)};
        d=d.Replace(layer);var session=new EditorSession(d);
        using(var edit=LayerTransformEdit.Begin(session))
        {
            edit.Preview(layer.Transform with {Width=20});
            Assert.Throws<NotSupportedException>(()=>edit.Complete());
        }
        Assert.Same(d,session.Document);Assert.False(session.InTransaction);Assert.False(session.CanUndo);
        var uniform=layer with {Mask=LayerMask.Solid(1,1,128),Transform=layer.Transform with {Width=20}};
        Assert.Same(uniform.Mask,ShapeRedraw.Apply(uniform).Mask);
    }
    [Fact] public void PixelReplacementClearsStyleWhileMetadataAndUndoPreserveIt()
    {
        var d = Document.Create(8,8);
        var style = new LayerShapeStyle(ShapeKind.Rectangle,1,0,0,2);
        var layer = d.Layers[0] with { Pixels = ShapeRaster.Create(style,8,8), Shape = style };
        d = d.Replace(layer);
        Assert.Equal(style,(layer with { Opacity=.5, Transform=layer.Transform with { X=5 }, Mask=LayerMask.Solid(8,8,128) }).Shape);
        Assert.Equal(style,(layer with { Pixels=layer.Pixels }).Shape);
        var session = new EditorSession(d);
        session.Apply(doc=>doc.Replace(layer with { Pixels=new Raster(8,8) }));
        Assert.Null(session.Document.Layers[0].Shape);
        session.Undo(); Assert.Equal(style,session.Document.Layers[0].Shape);
        session.Redo(); Assert.Null(session.Document.Layers[0].Shape);
        Assert.Throws<InvalidDataException>(()=>(d.Replace(Layer.Group("Group",8,8) with { Id=layer.Id, Shape=style })).Validate());
    }
    [Theory]
    [InlineData(ShapeKind.Rectangle)] [InlineData(ShapeKind.Ellipse)] [InlineData(ShapeKind.Line)]
    public void StyleAndPixelsRoundTripAndInvalidMetadataIsRejectedBeforePixels(ShapeKind kind)
    {
        string root=Path.Combine(Path.GetTempPath(),"Compositor-shape-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path=Path.Combine(root,"Shape.comp");
            var style=new LayerShapeStyle(kind,1,0,0,2,4,new(.25,.5),new(.75,.5));
            var d=Document.Create(16,16);
            var layer=d.Layers[0] with {Pixels=ShapeRaster.Create(style,16,16),Shape=style};
            d=d.Replace(layer); ProjectStore.Save(d,layer.Id,path);
            var loaded=ProjectStore.Load(path);
            Assert.Equal(style,loaded.Document.Layers[0].Shape);
            Assert.Equal(layer.Pixels.ToRgba(),loaded.Document.Layers[0].Pixels.ToRgba());
            string manifest=Path.Combine(path,"manifest.json");
            var json=JsonNode.Parse(File.ReadAllText(manifest))!;
            json["layers"]![0]!["shape"]!["red"]=-1;
            File.WriteAllText(manifest,json.ToJsonString());
            File.Delete(Path.Combine(path,"images",layer.Id.ToString().ToUpperInvariant()+".png"));
            Assert.Throws<InvalidDataException>(()=>ProjectStore.Load(path));
        }
        finally { Directory.Delete(root,true); }
    }
}
