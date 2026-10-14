using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;
public class BlurBrushTests
{
    private static Document Fixture()
    {
        var doc = Document.Create(513, 64);var bytes = new byte[513 * 64 * 4];
        for (int y = 0; y < 64; y++) for (int x = 0; x < 513; x++)
        { int p = (y * 513 + x) * 4;bytes[p] = bytes[p + 1] = bytes[p + 2] = x >= 256 ? (byte)255 : (byte)0;bytes[p + 3] = 255; }
        return doc.Replace(doc.Layers[0] with { Pixels = Raster.FromRgba(513, 64, bytes) });
    }
    [Fact]
    public void FrozenBlurSoftensTileBoundaryPreservesSourceMetadataAndUndo()
    {
        var doc = Fixture();var layer = doc.Layers[0] with { Mask = LayerMask.Solid(513, 64), Opacity = .7, Effects = new(ColorOverlay: new(.8, .2, .1, .4)) };doc = doc.Replace(layer);
        var source = layer.Pixels.ToRgba();var settings = new BrushSettings(40, 1, .5, 255, 0, 0);
        var sample = BlurBrush.Capture(doc, layer.Id, 40);var stroke = new BlurStroke(doc, layer.Id, settings);
        stroke.Append(new(255.5, 32.5));var first = stroke.Document.Layers[0].Pixels;var snapshot = first.ToRgba();
        stroke.Append(new(257.5, 32.5));var result = stroke.Complete();
        var reference = new CloneStroke(doc, layer.Id, settings, sample, new());reference.Append(new(255.5, 32.5));reference.Append(new(257.5, 32.5));
        Assert.Equal(reference.Complete().Layers[0].Pixels.ToRgba(), result.Layers[0].Pixels.ToRgba());
        var actual = result.Layers[0];var bytes = actual.Pixels.ToRgba();int p = (32 * 513 + 255) * 4;
        Assert.InRange(bytes[p], (byte)1, (byte)127);Assert.Equal((byte)255, bytes[p + 3]);
        Assert.Equal((byte)Math.Round(sample.ToRgba()[p] * .5, MidpointRounding.AwayFromZero), bytes[p]);
        var nextStroke = new BlurStroke(result, layer.Id, settings);nextStroke.Append(new(255.5, 32.5));
        Assert.True(nextStroke.Complete().Layers[0].Pixels.ToRgba()[p] > bytes[p]);
        Assert.Equal(source, layer.Pixels.ToRgba());Assert.Equal(snapshot, first.ToRgba());Assert.Same(layer.Mask, actual.Mask);Assert.Same(layer.Effects, actual.Effects);Assert.Equal(.7, actual.Opacity);
        var session = new EditorSession(doc);session.Apply(_ => result);Assert.Equal(1, session.UndoCount);session.Undo();Assert.Same(doc, session.Document);session.Redo();Assert.Same(result, session.Document);
        Assert.Throws<InvalidOperationException>(() => stroke.Append(new(1, 1)));
    }
    [Fact]
    public void SoftSampleExpandsTransparentEdgeWithoutMovingOriginalPixels()
    {
        var doc = Document.Create(64, 64);var layer = doc.Layers[0] with
        { Pixels = ShapeRaster.Create(new(ShapeKind.Rectangle, 1, 1, 1), 16, 16), Transform = new(20, 20, 16, 16), Mask = LayerMask.Solid(16, 16) };doc = doc.Replace(layer);
        var stroke = new BlurStroke(doc, layer.Id, new(10, 1, 1, 0, 0, 0));stroke.Append(new(35.5, 27.5));
        var result = stroke.Complete().Layers[0];var position = result.Transform.ToPixels(new(36.5, 27.5), result.Pixels.Width, result.Pixels.Height);
        int p = ((int)position.Y * result.Pixels.Width + (int)position.X) * 4;var bytes = result.Pixels.ToRgba();
        Assert.True(result.Pixels.Width > 16);Assert.InRange(bytes[p + 3], (byte)1, (byte)254);Assert.Equal(bytes[p], bytes[p + 3]);
        Assert.Equal(layer.Transform.X, result.Transform.X);Assert.Equal(16, layer.Pixels.Width);Assert.Equal(result.Pixels.Width, result.Mask!.Pixels.Width);
    }
    [Fact]
    public void SelectionExcludesBlurAndCancellationDoesNotChangeSource()
    {
        var doc = Fixture() with { Selection = SelectionGeometry.Box(0, 0, 10, 10, false, false) };var layer = doc.Layers[0];var source = layer.Pixels.ToRgba();
        var stroke = new BlurStroke(doc, layer.Id, new(40, 1, 1, 0, 0, 0));stroke.Append(new(255.5, 32.5));Assert.Same(doc, stroke.Complete());
        Assert.Throws<OperationCanceledException>(() => BlurBrush.Capture(doc, layer.Id, 40, new CancellationToken(true)));Assert.Equal(source, layer.Pixels.ToRgba());
        Assert.Equal(1.5, BlurBrush.Sigma(1));Assert.Equal(4, BlurBrush.Sigma(40));Assert.Equal(30, BlurBrush.Sigma(2000));Assert.Throws<ArgumentOutOfRangeException>(() => BlurBrush.Sigma(double.NaN));
    }
}
