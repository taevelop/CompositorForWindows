using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;
public class CloneStrokeTests
{
    [Fact]
    public void RotatedFlippedExpansionKeepsOriginalPixelsAtTheirDocumentPositions()
    {
        var doc = Document.Create(128, 128);var source = doc.Layers[0] with { Pixels = Red(4, 4), Transform = new(40, 40, 8, 8, 37, true, true) };doc = doc.Replace(source);
        var point = source.Transform.ToDocument(new(6.5, 1.5), 4, 4);
        var stroke = new CloneStroke(doc, source.Id, new(1, 1, 1, 0, 0, 0), Red(128, 128), new());stroke.Append(point);
        var grown = stroke.Document.Layers[0];Assert.True(grown.Pixels.Width > 4);var rgba = grown.Pixels.ToRgba();
        for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
        {
            var world = source.Transform.ToDocument(new(x + .5, y + .5), 4, 4);
            var local = grown.Transform.ToPixels(world, grown.Pixels.Width, grown.Pixels.Height);
            int p = ((int)Math.Floor(local.Y) * grown.Pixels.Width + (int)Math.Floor(local.X)) * 4;
            Assert.Equal(new byte[] { 100, 20, 10, 255 }, rgba.Skip(p).Take(4).ToArray());
            var mapped = grown.Transform.ToDocument(local, grown.Pixels.Width, grown.Pixels.Height);
            Assert.Equal(world.X, mapped.X, 8);Assert.Equal(world.Y, mapped.Y, 8);
        }
        Assert.Equal(37, grown.Transform.Rotation);Assert.True(grown.Transform.FlipX && grown.Transform.FlipY);
    }

    private static Raster Red(int width, int height) => Raster.FromRgba(width, height,
        Enumerable.Repeat(new byte[] { 100, 20, 10, 255 }, width * height).SelectMany(p => p).ToArray());

    [Fact]
    public void ExpansionReplaysPreviousCoverageAndPreservesMaskAndUndoSource()
    {
        var doc = Document.Create(16, 16);var source = doc.Layers[0] with { Pixels = Red(2, 2), Transform = new(4, 4, 2, 2), Mask = LayerMask.Solid(2, 2) };doc = doc.Replace(source);
        var stroke = new CloneStroke(doc, source.Id, new(1, 1, .5, 0, 0, 0), Red(16, 16), new());
        stroke.Append(new(12.5, 12.5));stroke.Append(new(14.5, 12.5));var result = stroke.Document;var layer = result.Layers[0];
        Assert.Equal(11, layer.Pixels.Width);Assert.Equal(9, layer.Pixels.Height);Assert.Equal(4, layer.Transform.X);Assert.Equal(4, layer.Transform.Y);
        var bytes = layer.Pixels.ToRgba();
        Assert.Equal(new byte[] { 50, 10, 5, 128 }, bytes.Skip((8 * 11 + 8) * 4).Take(4).ToArray());
        Assert.Equal(new byte[] { 50, 10, 5, 128 }, bytes.Skip((8 * 11 + 10) * 4).Take(4).ToArray());
        Assert.Equal(11, layer.Mask!.Pixels.Width);Assert.Equal((byte)255, layer.Mask.Pixels.ToRgba()[(8 * 11 + 10) * 4]);
        Assert.Equal(new byte[] { 100, 20, 10, 255 }, bytes.Take(4).ToArray());Assert.Equal(2, source.Pixels.Width);
        var session = new EditorSession(doc);session.Apply(_ => result);session.Undo();Assert.Same(doc, session.Document);session.Redo();Assert.Same(result, session.Document);
    }

    [Fact]
    public void IndependentMaskStaysPinnedAndTransparentCloneDoesNotExpandDocument()
    {
        var doc = Document.Create(16, 16);var source = doc.Layers[0] with { Pixels = Red(2, 2), Transform = new(4, 4, 2, 2), Mask = LayerMask.Solid(2, 2) with { Linked = false } };doc = doc.Replace(source);
        var stroke = new CloneStroke(doc, source.Id, new(1, 1, 1, 0, 0, 0), Red(16, 16), new());stroke.Append(new(1.5, 1.5));
        var layer = stroke.Document.Layers[0];Assert.Equal(source.Transform, layer.Mask!.Placement);Assert.Same(source.Mask.Pixels, layer.Mask.Pixels);
        var empty = new CloneStroke(doc, source.Id, new(1, 1, 1, 0, 0, 0), new Raster(16, 16), new());empty.Append(new(12.5, 12.5));Assert.Same(doc, empty.Document);
    }

    [Fact]
    public void ExpansionBudgetFailureLeavesLastDocumentUntouched()
    {
        var doc = Document.Create(10000, 10000);var source = doc.Layers[0] with { Pixels = Red(2, 2), Transform = new(0, 0, 1, 1) };doc = doc.Replace(source);
        var stroke = new CloneStroke(doc, source.Id, new(1, 1, 1, 0, 0, 0), new Raster(10000, 10000), new());
        Assert.Throws<InvalidDataException>(() => stroke.Append(new(9999, 9999)));Assert.Same(doc, stroke.Document);
    }
}
