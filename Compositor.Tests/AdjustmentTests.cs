using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

public sealed class AdjustmentTests
{
    [Theory]
    [InlineData(double.NaN, 0, 0)][InlineData(0, double.PositiveInfinity, 0)]
    [InlineData(0, 0, double.NegativeInfinity)][InlineData(101, 0, 0)][InlineData(0, -101, 0)][InlineData(0, 0, 101)]
    public void InvalidSettingsAreRejected(double brightness, double contrast, double saturation) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorAdjustments.Apply(new Raster(1, 1), new(brightness, contrast, saturation)));

    [Fact]
    public void NeutralAndTransparentEditsReuseTheOriginal()
    {
        var pixels = Raster.FromRgba(2, 1, new byte[] { 63, 20, 10, 128, 0, 0, 0, 0 });
        Assert.Same(pixels, ColorAdjustments.Apply(pixels, default));
        var blank = new Raster(513, 300);
        Assert.Same(blank, ColorAdjustments.Apply(blank, new(100, 100, 100)));
        var gray = Raster.FromRgba(1, 1, new byte[] { 64, 64, 64, 128 });
        Assert.Same(gray, ColorAdjustments.Apply(gray, new(Saturation: -100)));
    }

    [Theory]
    [InlineData(100, 0, 0, 128, 128, 128)]
    [InlineData(-100, 0, 0, 0, 0, 0)]
    [InlineData(0, -100, 0, 64, 64, 64)]
    [InlineData(0, 0, -100, 27, 27, 27)]
    [InlineData(0, 100, 0, 128, 0, 0)]
    public void KnownColorEndpointsPreserveAlpha(double b, double c, double s, byte r, byte g, byte blue)
    {
        var source = Raster.FromRgba(2, 1, new byte[] { 128, 0, 0, 128, 0, 0, 0, 0 });
        Assert.Equal(new byte[] { r, g, blue, 128, 0, 0, 0, 0 }, ColorAdjustments.Apply(source, new(b, c, s)).ToRgba());
        Assert.Equal(new byte[] { 128, 0, 0, 128, 0, 0, 0, 0 }, source.ToRgba());
    }

    [Fact]
    public void EveryAlphaRemainsPremultipliedAcrossTileEdgesAndPartialTiles()
    {
        const int width = 513, height = 257;
        var bytes = new byte[width * height * 4];
        for (int p = 0; p < bytes.Length; p += 4)
        {
            byte a = (byte)((p / 4 + 1) % 256); bytes[p] = a; bytes[p + 1] = (byte)(a / 2); bytes[p + 2] = (byte)(a / 3); bytes[p + 3] = a;
        }
        var source = Raster.FromRgba(width, height, bytes);
        foreach (var settings in new[] { new ColorAdjustment(28, 36, 70), new ColorAdjustment(-32, -22, -100) })
        {
            var edited = ColorAdjustments.Apply(source, settings); var actual = edited.ToRgba();
            for (int p = 0; p < bytes.Length; p += 4)
            {
                Assert.Equal(bytes[p + 3], actual[p + 3]);
                Assert.InRange(actual[p], 0, actual[p + 3]); Assert.InRange(actual[p + 1], 0, actual[p + 3]); Assert.InRange(actual[p + 2], 0, actual[p + 3]);
                // Equal source colors on opposite sides of each 256px boundary must remain equal.
                if (p >= 1024) Assert.Equal(actual.AsSpan(p - 1024, 4).ToArray(), actual.AsSpan(p, 4).ToArray());
            }
            var edge = edited.Tiles[new(2, 1)].Bytes;
            Assert.True(edge[4..].IndexOfAnyExcept((byte)0) < 0);
        }
        Assert.Equal(bytes, source.ToRgba());
    }

    [Fact]
    public void UnchangedTilesAreSharedAndSparseTilesAreNotCreated()
    {
        var bytes = new byte[768 * 4];
        for (int x = 0; x < 512; x++) { bytes[x * 4] = 128; bytes[x * 4 + 1] = x < 256 ? (byte)128 : (byte)0; bytes[x * 4 + 2] = x < 256 ? (byte)128 : (byte)0; bytes[x * 4 + 3] = 255; }
        var source = Raster.FromRgba(768, 1, bytes); var edited = ColorAdjustments.Apply(source, new(Saturation: -100));
        Assert.Same(source.Tiles[new(0, 0)], edited.Tiles[new(0, 0)]);
        Assert.NotSame(source.Tiles[new(1, 0)], edited.Tiles[new(1, 0)]);
        Assert.False(edited.Tiles.ContainsKey(new(2, 0)));
    }

    [Fact]
    public void CanceledWorkCannotMutateItsSource()
    {
        var source = Raster.FromRgba(1, 1, new byte[] { 80, 90, 100, 255 });
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => ColorAdjustments.Apply(source, new(40), cancel.Token));
        Assert.Equal(new byte[] { 80, 90, 100, 255 }, source.ToRgba());
    }

    [Fact]
    public void UndoEstimateCountsOnlyUniqueTilesRemovedFromTheWholeDocument()
    {
        var doc = Document.Create(8, 8);
        var source = LayerMask.Solid(8, 8, 100).Pixels;
        var first = doc.Layers[0] with { Pixels = source, Mask = LayerMask.Solid(1, 1, 128) };
        doc = doc.Replace(first); var next = doc.Replace(first with { Pixels = ColorAdjustments.Apply(source, new(30)) });
        Assert.Equal(PixelTile.ByteCount, EditorSession.UndoBytesRequired(doc, next));
        var shared = first with { Id = Guid.NewGuid(), Name = "Shared pixels" };
        Assert.Equal(0, EditorSession.UndoBytesRequired(doc with { Layers = doc.Layers.Add(shared) }, next with { Layers = next.Layers.Add(shared) }));
        Assert.Equal(0, EditorSession.UndoBytesRequired(doc, doc));
        Assert.Equal(256L * 1024 * 1024, EditorSession.MaxHistoryBytes);
    }

    [Fact]
    public void PreviewCancelNeutralCommitAndUndoPreserveHistoryAndMetadata()
    {
        var doc = Document.Create(8, 8); var group = Layer.Group("Group", 8, 8);
        var layer = doc.Layers[0] with { Pixels = LayerMask.Solid(8, 8, 100).Pixels, ParentId = group.Id,
            Mask = LayerMask.Solid(1, 1, 128), Transform = new(1, 2, 8, 8, 17), Blend = BlendMode.Screen, Opacity = .5 };
        doc = doc with { Layers = [group, layer] }; var session = new EditorSession(doc);
        session.Apply(d => d.Replace(layer with { Name = "Renamed" })); session.Undo();
        session.Begin(); session.Preview(doc.Replace(layer with { Pixels = ColorAdjustments.Apply(layer.Pixels, new(30)) }));
        session.Cancel(); Assert.Same(doc, session.Document); Assert.True(session.CanRedo); Assert.False(session.IsModified);
        session.Begin(); session.Preview(doc.Replace(layer with { Pixels = ColorAdjustments.Apply(layer.Pixels, default) })); session.Commit();
        Assert.Same(doc, session.Document); Assert.True(session.CanRedo); Assert.Equal(0, session.UndoCount);
        var next = doc.Replace(layer with { Pixels = ColorAdjustments.Apply(layer.Pixels, new(20, -10, -30)) });
        session.Apply(_ => next); Assert.Equal(1, session.UndoCount); Assert.False(session.CanRedo);
        Assert.Equal(layer, session.ActiveLayer! with { Pixels = layer.Pixels });
        Assert.Same(group, session.Document.Layers[0]); Assert.Same(layer.Mask, session.ActiveLayer!.Mask);
        session.Undo(); Assert.Same(doc, session.Document); Assert.False(session.IsModified);
        session.Redo(); Assert.Same(next, session.Document);
    }

    [Theory]
    [InlineData(1f)][InlineData(1.5f)][InlineData(2f)]
    public void AdjustedMaskedGroupedImageRendersAndSavesWithoutAdjustmentFields(float scale)
    {
        string root = Path.Combine(Path.GetTempPath(), "CompositorAdjustments-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var doc = Document.Create(520, 300); var group = Layer.Group("Group", 520, 300) with { Opacity = .7 };
            var layer = doc.Layers[0] with { Pixels = LayerMask.Solid(520, 300, 120).Pixels, Mask = LayerMask.Solid(1, 1, 128),
                ParentId = group.Id, Transform = new(8, 12, 500, 270, 17) };
            doc = doc with { Layers = [group, layer] };
            using var viewport = new ViewportRenderer();
            using var warm = viewport.Render(doc, (int)(600 * scale), (int)(400 * scale), scale, 0, 0);
            var edited = doc.Replace(layer with { Pixels = ColorAdjustments.Apply(layer.Pixels, new(22, 15, -40)) });
            using var incremental = viewport.Render(edited, (int)(600 * scale), (int)(400 * scale), scale, 0, 0);
            using var fresh = new ViewportRenderer(); using var expected = fresh.Render(edited, incremental.Width, incremental.Height, scale, 0, 0);
            Assert.Equal(Pixels(expected), Pixels(incremental));
            string project = Path.Combine(root, "Adjusted.comp"); ProjectStore.Save(edited, layer.Id, project);
            Assert.DoesNotContain("\"adjustment\"", File.ReadAllText(Path.Combine(project, "manifest.json")));
            var loaded = ProjectStore.Load(project).Document;
            Assert.Equal(edited.Layers[1].Pixels.ToRgba(), loaded.Layers[1].Pixels.ToRgba());
            using var renderer = new CanvasRenderer(); using var composite = renderer.Flatten(edited); using var reopened = renderer.Flatten(loaded);
            Assert.Equal(Pixels(composite), Pixels(reopened));
            string png = Path.Combine(root, "Adjusted.png"); ImageCodec.Export(edited, png, false);
            Assert.Equal(Pixels(composite), ImageCodec.Load(png).ToRgba());
        }
        finally
        {
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("CompositorAdjustments-")) throw new IOException("Unsafe cleanup.");
            Directory.Delete(root, true);
        }
    }
    private static byte[] Pixels(SKImage image)
    {
        using var bitmap = new SKBitmap(CanvasRenderer.Info(image.Width, image.Height));
        Assert.True(image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0)); return bitmap.GetPixelSpan().ToArray();
    }
}
