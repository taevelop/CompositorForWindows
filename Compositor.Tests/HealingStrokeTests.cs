using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;
public class HealingStrokeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExpandingWorkMatchesFullReplayAndPreservesEarlierSnapshots(bool selected, bool reverse)
    {
        var doc = Document.Create(1024, 512);
        // Cropping a solid tile leaves nonzero physical padding outside the logical source.
        var pixels = RasterReframe.Apply(ShapeRaster.Create(new(ShapeKind.Rectangle, .3, .5, .7), 256, 256), new(0, 0, 64, 64));
        var source = doc.Layers[0] with { Pixels = pixels, Transform = new(100, 100, 64, 64) };
        doc = doc.Replace(source);
        if (selected) doc = doc with { Selection = SelectionGeometry.Box(180, 100, 240, 70, false, true) with { Feather = 3 } };
        var settings = new BrushSettings(40, .7, .6, 0, 0, 0);
        var stroke = new HealingStroke(doc, source.Id, settings, HealingMode.CreateTexture, 123);
        var points = Enumerable.Range(0, 48).Select(i => new PointD(reverse ? 140.5 - i * 2 : 140.5 + i * 8, 140.5 + i % 3)).ToArray();
        stroke.Append(points[0]);var snapshot = stroke.Preview.Layers[0].Pixels;var saved = snapshot.ToRgba();
        foreach (var point in points.Skip(1)) stroke.Append(point);
        var basis = stroke.Base.Layers[0];
        var full = new BrushStroke(basis with { Pixels = new Raster(basis.Pixels.Width, basis.Pixels.Height), Mask = null }, settings with { Opacity = 1, Red = 255, Green = 255, Blue = 255 }, 1024, 512);
        var selection = doc.Selection is { } geometry ? SelectionCoverage.Create(geometry, 1024, 512) : null;
        var wash = new BrushStroke(basis, settings with { Opacity = .45, Red = 31, Green = 31, Blue = 31 }, 1024, 512, selection);
        foreach (var point in points) { full.Append(point);wash.Append(point); }
        var expected = full.Pixels.ToRgba();var coverage = stroke.Coverage();
        for (int i = 0; i < coverage.Length; i++) Assert.Equal(expected[i * 4 + 3], coverage[i]);
        Assert.Equal(wash.Pixels.ToRgba(), stroke.Preview.Layers[0].Pixels.ToRgba());
        Assert.Equal(saved, snapshot.ToRgba());Assert.Same(pixels, source.Pixels);
        var reference = SpotHealing.Compute(stroke.Base, source.Id, coverage, .6, HealingMode.CreateTexture, 123).Layers[0];
        var completed = stroke.Complete().Layers[0];
        var origin = basis.Transform.ToPixels(completed.Transform.ToDocument(new(0, 0), completed.Pixels.Width, completed.Pixels.Height), basis.Pixels.Width, basis.Pixels.Height);
        var cropped = RasterReframe.Apply(reference.Pixels, new((int)Math.Round(origin.X), (int)Math.Round(origin.Y), completed.Pixels.Width, completed.Pixels.Height));
        Assert.Equal(cropped.ToRgba(), completed.Pixels.ToRgba());
    }

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
