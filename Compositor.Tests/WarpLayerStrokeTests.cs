using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;
public class WarpLayerStrokeTests
{
    [Theory]
    [InlineData(SmearMode.Smudge, true)]
    [InlineData(SmearMode.Smudge, false)]
    [InlineData(SmearMode.Liquify, true)]
    [InlineData(SmearMode.Liquify, false)]
    public void RotatedFlippedWarpClipsCompositeFeatherOnceAndPreservesMasks(SmearMode mode, bool linked)
    {
        var doc = Document.Create(128, 128);var bytes = new byte[32 * 32 * 4];
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        { int p = (y * 32 + x) * 4;bytes[p + (x < 16 ? 0 : 2)] = 100;bytes[p + 3] = 128; }
        var transform = new LayerTransform(40, 40, 32, 32, 37, true, true);
        var mask = LayerMask.Solid(32, 32, 180) with { Linked = linked, Placement = linked ? null : new LayerTransform(30, 50, 32, 32, 23, true, false) };
        var layer = doc.Layers[0] with { Pixels = Raster.FromRgba(32, 32, bytes), Transform = transform, Mask = mask, Opacity = .6, Effects = new(ColorOverlay: new(.1, .2, .3, .4)) };doc = doc.Replace(layer);
        var a = transform.ToDocument(new(14.5, 15.5), 32, 32);var b = transform.ToDocument(new(18.5, 15.5), 32, 32);var settings = new BrushSettings(4, .7, .6, 0, 0, 0);
        var whole = new WarpLayerStroke(doc, layer.Id, settings, mode);whole.Append(a);whole.Append(b);var full = whole.Complete().Layers[0];
        var box = SelectionGeometry.Box(b.X - 5, b.Y - 5, 10, 10, false, true);
        var cut = SelectionGeometry.Box(b.X + 1, b.Y - 1, 2, 2, true, true);
        var combined = SelectionGeometry.Combine(box, cut, SelectionMode.Subtract, 128, 128) ?? throw new InvalidOperationException("Composite fixture is empty.");
        var geometry = combined with { Feather = 2 };
        var selected = doc with { Selection = geometry };var stroke = new WarpLayerStroke(selected, layer.Id, settings, mode);stroke.Append(a);stroke.Append(b);var result = stroke.Complete().Layers[0];
        var corner = result.Transform.ToDocument(new(0, 0), result.Pixels.Width, result.Pixels.Height);
        var initialOrigin = transform.ToPixels(corner, 32, 32);var fullOrigin = full.Transform.ToPixels(corner, full.Pixels.Width, full.Pixels.Height);
        var before = RasterReframe.Apply(layer.Pixels, new((int)Math.Round(initialOrigin.X), (int)Math.Round(initialOrigin.Y), result.Pixels.Width, result.Pixels.Height)).ToRgba();
        var raw = RasterReframe.Apply(full.Pixels, new((int)Math.Round(fullOrigin.X), (int)Math.Round(fullOrigin.Y), result.Pixels.Width, result.Pixels.Height)).ToRgba();var actual = result.Pixels.ToRgba();
        var coverage = SelectionCoverage.Create(geometry, 128, 128);int changed = 0;
        for (int y = 0; y < result.Pixels.Height; y++) for (int x = 0; x < result.Pixels.Width; x++)
        {
            var world = result.Transform.ToDocument(new(x + .5, y + .5), result.Pixels.Width, result.Pixels.Height);double weight = coverage.Sample(world);int p = (y * result.Pixels.Width + x) * 4;
            for (int c = 0; c < 4; c++)
            { int expected = (int)Math.Round(before[p + c] * (1 - weight) + raw[p + c] * weight, MidpointRounding.AwayFromZero);Assert.InRange(Math.Abs(actual[p + c] - expected), 0, 1);if (actual[p + c] != before[p + c]) changed++; }
        }
        Assert.True(changed > 0);Assert.Equal(bytes, layer.Pixels.ToRgba());Assert.Same(layer.Effects, result.Effects);Assert.Equal(layer.Opacity, result.Opacity);
        Assert.Equal(37, result.Transform.Rotation);Assert.True(result.Transform.FlipX && result.Transform.FlipY);Assert.Equal(mask.Linked, result.Mask!.Linked);
        if (!linked) { Assert.Same(mask.Pixels, result.Mask.Pixels);Assert.Equal(mask.Placement, result.Mask.Placement); }
        else Assert.Equal(result.Pixels.Width, result.Mask.Pixels.Width);
    }

    [Fact]
    public void ReplacementCopiesAlphaAndCanClearWhereOrdinaryCloneKeepsSource()
    {
        var doc = Document.Create(1, 1);var layer = doc.Layers[0] with { Pixels = Raster.FromRgba(1, 1, new byte[] { 100, 0, 0, 128 }) };doc = doc.Replace(layer);
        var sample = Raster.FromRgba(1, 1, new byte[] { 0, 20, 0, 32 });var settings = new BrushSettings(1, 1, .5, 0, 0, 0);
        var copy = new CloneStroke(doc, layer.Id, settings, sample, new(), true);copy.Append(new(.5, .5));
        Assert.Equal(new byte[] { 50, 10, 0, 80 }, copy.Complete().Layers[0].Pixels.ToRgba());
        var ordinary = new CloneStroke(doc, layer.Id, settings, sample, new());ordinary.Append(new(.5, .5));
        Assert.Equal(new byte[] { 94, 10, 0, 136 }, ordinary.Complete().Layers[0].Pixels.ToRgba());
        var clear = new CloneStroke(doc, layer.Id, settings with { Opacity = 1 }, new Raster(1, 1), new(), true);clear.Append(new(.5, .5));
        Assert.Equal(new byte[4], clear.Complete().Layers[0].Pixels.ToRgba());Assert.Equal((byte)128, layer.Pixels.ToRgba()[3]);
    }
    [Fact]
    public void ReplacementSurvivesAlignedGrowthAndFeatherClipsAlpha()
    {
        static Raster Red(int width, int height, byte red, byte alpha)
            => Raster.FromRgba(width, height, Enumerable.Repeat(new byte[] { red, 0, 0, alpha }, width * height).SelectMany(p => p).ToArray());
        var doc = Document.Create(512, 64);var layer = doc.Layers[0] with { Pixels = Red(64, 64, 100, 128), Transform = new(100, 0, 64, 64) };doc = doc.Replace(layer);
        var stroke = new CloneStroke(doc, layer.Id, new(40, 1, .5, 0, 0, 0), Red(512, 64, 40, 64), new(), true);
        stroke.Append(new(140.5, 31.5));var snapshot = stroke.Document.Layers[0].Pixels;var saved = snapshot.ToRgba();stroke.Append(new(400.5, 31.5));
        var result = stroke.Complete().Layers[0];var local = result.Transform.ToPixels(new(140.5, 31.5), result.Pixels.Width, result.Pixels.Height);
        int p = ((int)local.Y * result.Pixels.Width + (int)local.X) * 4;Assert.Equal((byte)96, result.Pixels.ToRgba()[p + 3]);Assert.Equal((byte)70, result.Pixels.ToRgba()[p]);Assert.Equal(saved, snapshot.ToRgba());
        var single = Document.Create(8, 8);var original = single.Layers[0] with { Pixels = Red(8, 8, 100, 128) };
        single = single.Replace(original) with { Selection = SelectionGeometry.Box(2, 2, 4, 4, false, true) with { Feather = 2 } };
        var feather = SelectionCoverage.Create(single.Selection!, 8, 8);var copy = new CloneStroke(single, original.Id, new(4, 1, 1, 0, 0, 0), new Raster(8, 8), new(), true);copy.Append(new(2.5, 3.5));
        byte expected = (byte)Math.Round(128 * (1 - (float)feather.Sample(new(2.5, 3.5))), MidpointRounding.AwayFromZero);
        Assert.Equal(expected, copy.Complete().Layers[0].Pixels.ToRgba()[(3 * 8 + 2) * 4 + 3]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BrushStroke(original, new(2004, 1, 1, 0, 0, 0), 8, 8));
        _ = new BrushStroke(original, new(2004, 1, 1, 0, 0, 0), 8, 8, cloneSample: new Raster(8, 8), replaceClone: true);
    }
    private static Document Fixture()
    {
        var doc = Document.Create(32, 32);var bytes = new byte[32 * 32 * 4];int a = (10 * 32 + 10) * 4, b = a + 4;
        bytes[a] = 128;bytes[a + 3] = 128;bytes[b + 1] = 64;bytes[b + 3] = 128;
        return doc.Replace(doc.Layers[0] with { Pixels = Raster.FromRgba(32, 32, bytes), Mask = LayerMask.Solid(32, 32), Opacity = .7, Effects = new(ColorOverlay: new(.2, .3, .4, .5)) });
    }
    [Fact]
    public void SmudgeCopiesRawResultOnceWithMetadataUndoAndStorage()
    {
        var doc = Fixture();var source = doc.Layers[0];var bytes = source.Pixels.ToRgba();
        var stroke = new WarpLayerStroke(doc, source.Id, new(2, 1, .5, 0, 0, 0), SmearMode.Smudge);stroke.Append(new(10, 10));stroke.Append(new(11, 10));var result = stroke.Complete();
        var layer = result.Layers[0];int p = (10 * 32 + 11) * 4;
        Assert.Equal(new byte[] { 128, 0, 0, 128 }, layer.Pixels.ToRgba().AsSpan(p, 4).ToArray());Assert.Equal(bytes, source.Pixels.ToRgba());Assert.Same(source.Mask, layer.Mask);Assert.Same(source.Effects, layer.Effects);Assert.Equal(.7, layer.Opacity);
        Assert.Same(result, stroke.Complete());Assert.Throws<InvalidOperationException>(() => stroke.Append(new(12, 10)));
        var session = new EditorSession(doc);session.Apply(_ => result);Assert.Equal(1, session.UndoCount);session.Undo();Assert.Same(doc, session.Document);session.Redo();Assert.Same(result, session.Document);
        string directory = Path.Combine(Path.GetTempPath(), "warp-roundtrip-" + Guid.NewGuid().ToString("N") + ".comp");
        try { ProjectStore.Save(result, source.Id, directory);Assert.Equal(layer.Pixels.ToRgba(), ProjectStore.Load(directory).Document.Layers[0].Pixels.ToRgba()); }
        finally
        {
            string resolved = Path.GetFullPath(directory), parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("warp-roundtrip-")) throw new IOException("Unsafe cleanup.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }
    [Fact]
    public void ExcludedSelectionClickAndCancellationDoNotPublish()
    {
        var doc = Fixture() with { Selection = SelectionGeometry.Box(0, 0, 1, 1, false, false) };var source = doc.Layers[0];
        var stroke = new WarpLayerStroke(doc, source.Id, new(2, 1, 1, 0, 0, 0), SmearMode.Smudge);stroke.Append(new(10, 10));stroke.Append(new(11, 10));Assert.Same(doc, stroke.Complete());
        var click = new WarpLayerStroke(doc, source.Id, new(2, 1, 1, 0, 0, 0), SmearMode.Liquify);click.Append(new(10, 10));Assert.Same(doc, click.Complete());
        var cancelled = new WarpLayerStroke(doc, source.Id, new(2, 1, 1, 0, 0, 0), SmearMode.Smudge);cancelled.Append(new(10, 10));
        Assert.Throws<OperationCanceledException>(() => cancelled.Append(new(11, 10), new CancellationToken(true)));Assert.Throws<InvalidOperationException>(() => cancelled.Complete());
        Assert.Throws<OperationCanceledException>(() => stroke.Complete(new CancellationToken(true)));
    }
}
