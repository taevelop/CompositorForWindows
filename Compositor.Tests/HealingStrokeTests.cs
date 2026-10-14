using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;
public class HealingStrokeTests
{
    [Fact]
    public void RotatedFlippedExpandedStrokeAppliesFeatherToFinalHealingOnce()
    {
        var doc = Fixture(false);var source = doc.Layers[0] with { Transform = new(20, 20, 16, 16, 37, true, true) };doc = doc.Replace(source);
        var center = source.Transform.ToDocument(new(15.5, 7.5), 16, 16);
        doc = doc with { Selection = SelectionGeometry.Box(center.X - 2, center.Y - 2, 4, 4, false, true) with { Feather = 1 } };
        var stroke = new HealingStroke(doc, source.Id, new(3, .7, .7, 0, 0, 0), HealingMode.ContentAware, 123);stroke.Append(center);
        var basis = stroke.Base.Layers[0];var raw = SpotHealing.Apply(basis.Pixels, stroke.Coverage(), .7, seed: 123).ToRgba();var before = basis.Pixels.ToRgba();
        var result = stroke.Complete().Layers[0];var actual = result.Pixels.ToRgba();var selection = SelectionCoverage.Create(doc.Selection!, 64, 64);
        for (int y = 0; y < result.Pixels.Height; y++) for (int x = 0; x < result.Pixels.Width; x++)
        {
            var world = result.Transform.ToDocument(new(x + .5, y + .5), result.Pixels.Width, result.Pixels.Height);
            var local = basis.Transform.ToPixels(world, basis.Pixels.Width, basis.Pixels.Height);int index = ((int)Math.Floor(local.Y) * basis.Pixels.Width + (int)Math.Floor(local.X)) * 4;
            double weight = selection.Sample(world);
            for (int c = 0; c < 4; c++)
            {
                int expected = (int)Math.Round(before[index + c] * (1 - weight) + raw[index + c] * weight, MidpointRounding.AwayFromZero);
                Assert.InRange(Math.Abs(actual[(y * result.Pixels.Width + x) * 4 + c] - expected), 0, 1);
            }
        }
        Assert.Equal(source.Transform, result.Mask!.Placement);Assert.Same(source.Mask!.Pixels, result.Mask.Pixels);
        Assert.Equal(37, result.Transform.Rotation);Assert.True(result.Transform.FlipX && result.Transform.FlipY);
    }

    private static Document Fixture(bool linked = true)
    {
        var doc = Document.Create(64, 64);var bytes = Enumerable.Repeat(new byte[] { 80, 120, 160, 255 }, 256).SelectMany(p => p).ToArray();
        int p = (7 * 16 + 15) * 4;bytes[p] = bytes[p + 1] = bytes[p + 2] = 0;
        return doc.Replace(doc.Layers[0] with { Pixels = Raster.FromRgba(16, 16, bytes), Transform = new(20, 20, 16, 16), Mask = LayerMask.Solid(16, 16) with { Linked = linked } });
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExpansionPreservesPixelPositionsMaskRulesAndSource(bool linked)
    {
        var doc = Fixture(linked);var source = doc.Layers[0];var original = source.Pixels.ToRgba();
        var stroke = new HealingStroke(doc, source.Id, new(3, 1, 1, 0, 0, 0), HealingMode.ContentAware, 123);stroke.Append(new(35.5, 27.5));
        Assert.Equal(17, stroke.Base.Layers[0].Pixels.Width);Assert.Equal(original, source.Pixels.ToRgba());
        var result = stroke.Complete();var layer = result.Layers[0];Assert.Equal(17, layer.Pixels.Width);Assert.Equal(20, layer.Transform.X);Assert.Equal(20, layer.Transform.Y);
        Assert.True(layer.Pixels.ToRgba()[(7 * 17 + 16) * 4 + 3] > 0);
        if (linked) { Assert.Equal(17, layer.Mask!.Pixels.Width);Assert.Equal((byte)255, layer.Mask.Pixels.ToRgba()[(7 * 17 + 16) * 4]); }
        else { Assert.Same(source.Mask!.Pixels, layer.Mask!.Pixels);Assert.Equal(source.Transform, layer.Mask.Placement); }
        var session = new EditorSession(doc);session.Apply(_ => result);session.Undo();Assert.Same(doc, session.Document);session.Redo();Assert.Same(result, session.Document);
    }
    [Fact]
    public void SelectionExcludingExpandedAreaReturnsOriginalAndCancellationDoesNotPublish()
    {
        var doc = Fixture() with { Selection = new("M0 0L1 0L1 1L0 1Z") { Antialiased = false } };var source = doc.Layers[0];
        var stroke = new HealingStroke(doc, source.Id, new(3, 1, 1, 0, 0, 0), HealingMode.ContentAware, 123);stroke.Append(new(35.5, 27.5));
        Assert.Same(doc, stroke.Complete());Assert.Throws<OperationCanceledException>(() => stroke.Complete(new CancellationToken(true)));
    }
}
