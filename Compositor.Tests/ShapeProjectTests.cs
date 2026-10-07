using Compositor.Core;
using Compositor.Imaging;
using System.Text.Json.Nodes;
using Xunit;

namespace Compositor.Tests;

public sealed class ShapeProjectTests
{
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
