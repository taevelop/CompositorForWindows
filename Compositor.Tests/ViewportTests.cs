using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

public class ViewportTests
{
    [Fact] public void IndependentMaskMovementAndPaintingInvalidateViewportAndSampling()
    {
        var d=Document.Create(600,40);var pixels=ShapeRaster.Create(new(ShapeKind.Rectangle,1,0,0),600,40);
        var seed=LayerMask.Solid(8,8,0);var bytes=seed.Pixels.Tiles[new(0,0)].Bytes.ToArray();
        for(int y=2;y<6;y++)for(int x=2;x<6;x++)for(int c=0;c<3;c++)bytes[(y*256+x)*4+c]=255;
        var mask=seed.WithPixels(new Raster(8,8,seed.Pixels.Tiles.SetItem(new(0,0),new PixelTile(bytes)))) with{Placement=new(380,3,40,32),Linked=false};
        d=d.Replace(d.Layers[0] with{Pixels=pixels,Mask=mask});
        using var view=new ViewportRenderer();using var canonical=new CanvasRenderer();
        void Compare(Document doc)
        {
            using var actual=view.Render(doc,600,40,1,0,0);using var expected=canonical.Flatten(doc);
            using var a=new SKBitmap(CanvasRenderer.Info(600,40));using var b=new SKBitmap(a.Info);
            actual.ReadPixels(a.Info,a.GetPixels(),a.RowBytes,0,0);expected.ReadPixels(b.Info,b.GetPixels(),b.RowBytes,0,0);
            Assert.Equal(b.GetPixelSpan().ToArray(),a.GetPixelSpan().ToArray());
        }
        Compare(d);
        var moved=d.Replace(d.Layers[0] with{Mask=mask with{Placement=mask.Placement! with{X=440}}});Compare(moved);
        var painted=moved.Replace(moved.Layers[0] with{Mask=moved.Layers[0].Mask!.WithPixels(LayerMask.Solid(8,8,255).Pixels)});Compare(painted);
        Compare(d);Compare(painted);
    }
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void IncrementalViewportMatchesFreshCompositingAfterPaintEraseUndoAndStructuralEdits(float scale)
    {
        int width = (int)(330 * scale), height = (int)(280 * scale);
        var doc = Document.Create(600, 500);
        var background = new BrushStroke(doc.Layers[0], new(1800, 1, 1, 90, 110, 160), 600, 500);
        background.Append(new(300, 250)); doc = doc.Replace(doc.Layers[0] with { Pixels = background.Pixels });
        var l = Layer.Blank("Transformed", 400, 400) with { Transform = new(80, 50, 360, 420, 17), Opacity = .6, Blend = BlendMode.Multiply };
        doc = doc with { Layers = doc.Layers.Add(l) };
        using var viewport = new ViewportRenderer();
        void Compare(Document d)
        {
            using var incremental = viewport.Render(d, width, height, .5f * scale, 12 * scale, 7 * scale);
            using var reference = SKSurface.Create(CanvasRenderer.Info(width, height));
            reference.Canvas.Clear(SKColors.Transparent); reference.Canvas.Translate(12 * scale, 7 * scale); reference.Canvas.Scale(.5f * scale);
            using var renderer = new CanvasRenderer(); renderer.Draw(reference.Canvas, d);
            using var expected = reference.Snapshot();
            using var a = new SKBitmap(CanvasRenderer.Info(width, height)); using var b = new SKBitmap(a.Info);
            incremental.ReadPixels(a.Info, a.GetPixels(), a.RowBytes, 0, 0); expected.ReadPixels(b.Info, b.GetPixels(), b.RowBytes, 0, 0);
            Assert.True(a.GetPixelSpan().SequenceEqual(b.GetPixelSpan()), "Incremental and full render pixels differ.");
        }
        Compare(doc); var original = doc;
        var brush = new BrushStroke(l, new(100, .6, .5, 190, 20, 80), 600, 500);
        for (int i = 0; i < 6; i++) { brush.Append(new(100 + i * 60, 100 + i * 30)); doc = doc.Replace(l with { Pixels = brush.Pixels }); Compare(doc); }
        var erase = new BrushStroke(doc.Layers[1], new(80, .2, 1, 0, 0, 0, true), 600, 500);
        erase.Append(new(220, 160)); doc = doc.Replace(doc.Layers[1] with { Pixels = erase.Pixels }); Compare(doc);
        Compare(original); Compare(doc);
        doc = doc.Replace(doc.Layers[1] with { Visible = false }); Compare(doc);
        doc = doc with { Layers = doc.Layers.RemoveAt(1) }; Compare(doc);
    }
}
