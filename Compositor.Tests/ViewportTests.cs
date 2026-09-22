using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

public class ViewportTests
{
    [Fact]
    public void IncrementalViewportMatchesFreshCompositingAfterPaintEraseUndoAndStructuralEdits()
    {
        var doc = Document.Create(600, 500);
        var background = new BrushStroke(doc.Layers[0], new(1800, 1, 1, 90, 110, 160), 600, 500);
        background.Append(new(300, 250)); doc = doc.Replace(doc.Layers[0] with { Pixels = background.Pixels });
        var l = Layer.Blank("Transformed", 400, 400) with { Transform = new(80, 50, 360, 420, 17), Opacity = .6, Blend = BlendMode.Multiply };
        doc = doc with { Layers = doc.Layers.Add(l) };
        using var viewport = new ViewportRenderer();
        void Compare(Document d)
        {
            using var incremental = viewport.Render(d, 330, 280, .5f, 12, 7);
            using var reference = SKSurface.Create(CanvasRenderer.Info(330, 280));
            reference.Canvas.Clear(SKColors.Transparent); reference.Canvas.Translate(12, 7); reference.Canvas.Scale(.5f);
            using var renderer = new CanvasRenderer(); renderer.Draw(reference.Canvas, d);
            using var expected = reference.Snapshot();
            using var a = new SKBitmap(CanvasRenderer.Info(330, 280)); using var b = new SKBitmap(a.Info);
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
