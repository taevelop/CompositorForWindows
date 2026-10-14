using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;
public class MaskTracingTests
{
    [Theory]
    [InlineData(0,false)]
    [InlineData(35,true)]
    public void LayerAlphaIgnoresMaskAndMapsTransformWhileMaskUsesIndependentPlacement(double angle,bool flip)
    {
        var doc=Document.Create(100,100);var transform=new LayerTransform(20,20,20,20,Rotation:angle,FlipX:flip);
        var layer=doc.Layers[0] with{Pixels=ShapeRaster.Create(new(ShapeKind.Rectangle,.2,.4,.6),4,4),Transform=transform,Mask=LayerMask.Solid(4,4,0) with{Placement=new(60,60,8,8),Linked=false}};doc=doc.Replace(layer);
        var image=MaskTracing.LoadSelection(doc,layer.Id,false);var hidden=MaskTracing.LoadSelection(doc,layer.Id,true);
        Assert.True(SelectionGeometry.Contains(image.Selection!,transform.ToDocument(new(2,2),4,4)));Assert.False(SelectionGeometry.Contains(image.Selection!,new(64,64)));
        Assert.True(SelectionGeometry.Contains(hidden.Selection!,new(64,64)));Assert.False(SelectionGeometry.Contains(hidden.Selection!,new(30,30)));
        Assert.Same(layer.Pixels,image.Layers[0].Pixels);Assert.Same(layer.Mask,hidden.Layers[0].Mask);
    }

    [Fact]
    public void SubjectInferenceReceivesVisibleCanvasCompositeRatherThanActiveLayer()
    {
        var doc=Document.Create(8,8);var image=doc.Layers[0] with{Id=Guid.NewGuid(),Pixels=ShapeRaster.Create(new(ShapeKind.Rectangle,1,0,0),2,2),Transform=new(4,3,2,2)};
        var hidden=image with{Id=Guid.NewGuid(),Visible=false,Transform=new(0,0,2,2)};doc=doc with{Layers=doc.Layers.Add(image).Add(hidden)};
        int calls=0;
        var result=SubjectSelection.Compute(doc,(source,token)=>
        {
            calls++;Assert.Equal(8,source.Width);Assert.Equal(8,source.Height);var bytes=source.ToRgba();
            Assert.Equal(255,bytes[(3*8+4)*4]);Assert.Equal(255,bytes[(3*8+4)*4+3]);Assert.Equal(0,bytes[3]);return LayerMask.Solid(8,8);
        });
        Assert.Equal(1,calls);Assert.Same(doc.Layers[0].Pixels,result.Layers[0].Pixels);Assert.True(SelectionGeometry.Contains(result.Selection!,new(7,7)));
    }

    [Fact]
    public void ThresholdHolesDiagonalContactsAndTileEdgesMatchPixelCenters()
    {
        const int w=263,h=9;var tiles=LayerMask.Solid(w,h,0).Pixels.Tiles.ToBuilder();
        foreach(var(key,tile) in tiles.ToArray())
        {
            var bytes=tile.Bytes.ToArray();for(int y=0;y<Math.Min(256,h-key.Y*256);y++)for(int x=0;x<Math.Min(256,w-key.X*256);x++)
            {
                int px=key.X*256+x,py=key.Y*256+y;byte value=(byte)((px>=252&&px<=260&&py>=1&&py<=7&&!(px>=254&&px<=258&&py>=3&&py<=5))||px==1&&py==1||px==2&&py==2?128:127);
                int p=(y*256+x)*4;bytes[p]=bytes[p+1]=bytes[p+2]=value;
            }
            tiles[key]=new(bytes);
        }
        var mask=new LayerMask(new Raster(w,h,tiles.ToImmutable()));var selection=MaskTracing.WhitePixels(mask);using var path=SelectionGeometry.Path(selection);
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)Assert.Equal(mask.Pixels.Tiles[new(x/256,0)].Bytes[(y*256+x%256)*4]>=128,path.Contains(x+.5f,y+.5f));
        Assert.Throws<OperationCanceledException>(()=>MaskTracing.WhitePixels(mask,new CancellationToken(true)));
    }
    [Fact]
    public void SubjectSelectionCombinesWithoutChangingImageAndSupportsUndo()
    {
        var doc=Document.Create(8,8) with{Selection=SelectionGeometry.Box(0,0,2,2)};var mask=LayerMask.Solid(8,8);
        var result=MaskTracing.SelectSubject(doc,mask,SelectionMode.Subtract);
        Assert.False(SelectionGeometry.Contains(result.Selection!,new(1,1)));Assert.Same(doc.Layers[0].Pixels,result.Layers[0].Pixels);
        var session=new EditorSession(doc);session.Begin();session.Preview(result);session.Commit();session.Undo();Assert.Same(doc,session.Document);
        Assert.Throws<InvalidOperationException>(()=>MaskTracing.SelectSubject(doc,LayerMask.Solid(8,8,0)));
    }
}
