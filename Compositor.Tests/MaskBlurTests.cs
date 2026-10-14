using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;
public class MaskBlurTests
{
    [Fact]
    public void RotatedFlippedMaskUsesDocumentSampleAndFinalFeatherWithoutGrowingGrid()
    {
        var mask = LayerMask.Solid(32, 32);var tiles = mask.Pixels.Tiles.ToBuilder();var bytes = tiles[new(0, 0)].Bytes.ToArray();
        for (int y = 0; y < 32; y++) for (int x = 16; x < 32; x++) { int p = (y * 256 + x) * 4;bytes[p] = bytes[p + 1] = bytes[p + 2] = 0; }
        tiles[new(0, 0)] = new(bytes);var placement = new LayerTransform(40, 40, 32, 32, 37, true, true);
        mask = mask.WithPixels(new(32, 32, tiles.ToImmutable())) with { Placement = placement, Linked = false };
        var doc = Document.Create(128, 128);var layer = doc.Layers[0] with { Mask = mask };doc = doc.Replace(layer);
        var center = placement.ToDocument(new(15.5, 15.5), 32, 32);
        var geometry = SelectionGeometry.Box(center.X - 5, center.Y - 5, 10, 10, false, true) with { Feather = 3 };
        var selection = SelectionCoverage.Create(geometry, 128, 128);var sample = BlurBrush.CaptureMask(doc, layer.Id, 40);
        var settings = new BrushSettings(40, 1, .6, 0, 0, 0);
        var stroke = new MaskStroke(layer, settings, 128, 128, selection, sample);stroke.Append(center);
        var actual = stroke.Mask.Pixels.ToRgba();var original = mask.Pixels.ToRgba();int checkedPixels = 0;
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            var world = placement.ToDocument(new(x + .5, y + .5), 32, 32);int p = (y * 32 + x) * 4;
            if (double.Hypot(world.X - center.X, world.Y - center.Y) < 18)
            {
                var softened = CloneStamp.Sample(sample, world);double amount = .6 * (float)selection.Sample(world);
                int expected = (int)Math.Round(softened.Red * amount + original[p] * (1 - softened.Alpha * amount / 255), MidpointRounding.AwayFromZero);
                Assert.InRange(Math.Abs(actual[p] - expected), 0, 1);checkedPixels++;
            }
            Assert.Equal(actual[p], actual[p + 1]);Assert.Equal(actual[p], actual[p + 2]);Assert.Equal((byte)255, actual[p + 3]);
        }
        Assert.True(checkedPixels > 100);Assert.Equal(placement, stroke.Mask.Placement);Assert.Equal(32, stroke.Mask.Pixels.Width);Assert.Equal(32, stroke.Mask.Pixels.Height);
        var snapshot = stroke.Mask;var saved = snapshot.Pixels.ToRgba();stroke.Append(new(0, 0));Assert.Equal(saved, snapshot.Pixels.ToRgba());
        var outside = new MaskStroke(layer, settings, 128, 128, softenedSample: sample);outside.Append(new(0, 0));Assert.Same(mask, outside.Mask);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    [InlineData(255)]
    public void ConstantMaskKeepsCanvasEdgeToneAndOpaqueGray(int gray)
    {
        var doc = Document.Create(64, 64);var layer = doc.Layers[0] with { Mask = LayerMask.Solid(1, 1, (byte)gray) };doc = doc.Replace(layer);
        var sample = BlurBrush.CaptureMask(doc, layer.Id, 40).ToRgba();
        for (int p = 0; p < sample.Length; p += 4)
        { Assert.Equal((byte)gray, sample[p]);Assert.Equal(sample[p], sample[p + 1]);Assert.Equal(sample[p], sample[p + 2]);Assert.Equal((byte)255, sample[p + 3]); }
    }
    [Fact]
    public void PlacedMaskBlurPreservesPlacementPixelsSelectionAndHistory()
    {
        var mask = LayerMask.Solid(32, 32);var tiles = mask.Pixels.Tiles.ToBuilder();var bytes = tiles[new(0, 0)].Bytes.ToArray();
        for (int y = 0; y < 32; y++) for (int x = 16; x < 32; x++) { int p = (y * 256 + x) * 4;bytes[p] = bytes[p + 1] = bytes[p + 2] = 0; }
        tiles[new(0, 0)] = new(bytes);mask = mask.WithPixels(new(32, 32, tiles.ToImmutable())) with { Placement = new(20, 20, 32, 32), Linked = false };
        var doc = Document.Create(80, 80);var layer = doc.Layers[0] with { Mask = mask };doc = doc.Replace(layer);
        var sample = BlurBrush.CaptureMask(doc, layer.Id, 20);var settings = new BrushSettings(20, 1, .5, 255, 0, 0);
        var stroke = new MaskStroke(layer, settings, 80, 80, softenedSample: sample);stroke.Append(new(35.5, 35.5));
        var result = stroke.Mask;Assert.NotSame(mask, result);Assert.Equal(mask.Placement, result.Placement);Assert.False(result.Linked);
        int index = (15 * 32 + 15) * 4;Assert.InRange(result.Pixels.ToRgba()[index], (byte)129, (byte)254);
        Assert.Equal((byte)255, mask.Pixels.ToRgba()[index]);
        var edited = doc.Replace(layer with { Mask = result });Assert.Same(layer.Pixels, edited.Layers[0].Pixels);
        var session = new EditorSession(doc);session.Apply(_ => edited);session.Undo();Assert.Same(doc, session.Document);session.Redo();Assert.Same(edited, session.Document);
        var selection = SelectionCoverage.Create(SelectionGeometry.Box(0, 0, 1, 1, false, false), 80, 80);
        var excluded = new MaskStroke(layer, settings, 80, 80, selection, sample);excluded.Append(new(35.5, 35.5));Assert.Same(mask, excluded.Mask);
        Assert.Throws<OperationCanceledException>(() => BlurBrush.CaptureMask(doc, layer.Id, 20, new CancellationToken(true)));
        Assert.Throws<InvalidOperationException>(() => BlurBrush.CaptureMask(doc.Replace(layer with { Mask = mask with { Enabled = false } }), layer.Id, 20));
    }
}
