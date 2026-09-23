using System.Text.Json.Nodes;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

public sealed class MaskTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "CompositorMasks-" + Guid.NewGuid().ToString("N"));
    public MaskTests() => Directory.CreateDirectory(root);
    public void Dispose()
    {
        if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("CompositorMasks-")) throw new IOException("Unsafe cleanup.");
        Directory.Delete(root, true);
    }
    private static Document Sample(int width = 520, int height = 300)
    {
        var doc = Document.Create(width, height);
        var brush = new BrushStroke(doc.Layers[0], new(2000, 1, .5, 200, 100, 50), width, height);
        brush.Append(new(width / 2d, height / 2d));
        return doc.Replace(doc.Layers[0] with { Pixels = brush.Pixels, Mask = LayerMask.Solid(1, 1) });
    }
    private static byte[] Render(Document doc)
    {
        using var renderer = new CanvasRenderer(); using var image = renderer.Flatten(doc);
        using var bitmap = new SKBitmap(CanvasRenderer.Info(doc.Width, doc.Height));
        Assert.True(image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0));
        return bitmap.GetPixelSpan().ToArray();
    }

    [Theory]
    [InlineData(0)][InlineData(128)][InlineData(255)]
    public void CoverageMultipliesPremultipliedChannelsAndDisabledRestoresSource(byte gray)
    {
        var doc = Sample(16, 16); var layer = doc.Layers[0];
        var original = Render(doc); var masked = doc.Replace(layer with { Mask = LayerMask.Solid(1, 1, gray) });
        var pixels = Render(masked);
        for (int i = 0; i < pixels.Length; i++) Assert.Equal((byte)((original[i] * gray + 127) / 255), pixels[i]);
        Assert.Equal(original, Render(masked.Replace(masked.Layers[0] with { Mask = masked.Layers[0].Mask! with { Enabled = false } })));
    }

    [Fact]
    public void MaskChangesCoverageAcrossTileBoundariesWithoutChangingSource()
    {
        var doc = Sample(); var layer = doc.Layers[0]; var original = layer.Pixels.ToRgba();
        var stroke = new MaskStroke(layer, new(90, 1, .5, 0, 0, 0), doc.Width, doc.Height);
        stroke.Append(new(220, 150)); stroke.Append(new(290, 150));
        var mask = stroke.Mask; var coverage = mask.Pixels.ToRgba();
        Assert.InRange(coverage[(150 * doc.Width + 255) * 4], (byte)127, (byte)128);
        Assert.Equal(coverage[(150 * doc.Width + 255) * 4], coverage[(150 * doc.Width + 256) * 4]);
        stroke.Append(new(220, 150)); Assert.Equal(coverage, stroke.Mask.Pixels.ToRgba());
        Assert.Equal(original, layer.Pixels.ToRgba()); Assert.Equal((byte)255, layer.Mask!.Pixels.ToRgba()[0]);
        var reveal = new MaskStroke(layer with { Mask = mask }, new(30, 1, 1, 255, 255, 255), doc.Width, doc.Height);
        reveal.Append(new(256, 150)); Assert.Equal((byte)255, reveal.Mask.Pixels.ToRgba()[(150 * doc.Width + 256) * 4]);
        var erase = new MaskStroke(layer with { Mask = reveal.Mask }, new(30, 1, 1, 255, 255, 255, true), doc.Width, doc.Height);
        erase.Append(new(256, 150)); Assert.Equal((byte)0, erase.Mask.Pixels.ToRgba()[(150 * doc.Width + 256) * 4]);
    }

    [Fact]
    public void UnchangedUniformMaskStrokePreservesIdentity()
    {
        var doc = Sample(); var stroke = new MaskStroke(doc.Layers[0], new(80, 1, 1, 255, 255, 255), doc.Width, doc.Height);
        stroke.Append(new(100, 100)); Assert.Same(doc.Layers[0].Mask, stroke.Mask);
        Assert.Throws<InvalidOperationException>(() => new MaskStroke(doc.Layers[0] with { Mask = doc.Layers[0].Mask! with { Enabled = false } }, new(40, 1, 1, 0, 0, 0), doc.Width, doc.Height));
    }

    [Fact]
    public void UndoRestoresMaskPixelsSelectionAndAccountsForHistoryTiles()
    {
        var doc = Sample(); var session = new EditorSession(doc) { EditMask = true };
        var stroke = new MaskStroke(doc.Layers[0], new(80, 1, 1, 0, 0, 0), doc.Width, doc.Height); stroke.Append(new(256, 150));
        session.Apply(d => d.Replace(d.Layers[0] with { Mask = stroke.Mask }));
        var edited = session.Document; session.MarkSaved();
        session.Apply(d => d.Replace(d.Layers[0] with { Mask = null }));
        Assert.False(session.EditMask); Assert.True(session.HistoryRetainedBytes >= stroke.Mask.Pixels.Tiles.Values.Distinct().Count() * (long)PixelTile.ByteCount);
        session.Undo(); Assert.Same(edited, session.Document); Assert.True(session.EditMask); Assert.False(session.IsModified);
        session.Undo(); Assert.Same(doc.Layers[0].Mask, session.ActiveLayer!.Mask);
        session.Redo(); Assert.Same(stroke.Mask, session.ActiveLayer!.Mask);
        session.Begin(); session.Preview(doc); session.Cancel(); Assert.Same(stroke.Mask, session.ActiveLayer!.Mask);
    }

    [Theory]
    [InlineData(1f, 0, false)][InlineData(1.5f, 0, false)][InlineData(2f, 0, false)]
    [InlineData(1f, 17, false)][InlineData(1.5f, 17, false)][InlineData(2f, 17, false)]
    [InlineData(1.5f, 0, true)]
    public void IncrementalMaskPaintToggleTransformAndUndoMatchFreshRender(float scale, double rotation, bool rotatedOverlay)
    {
        var doc = Sample(); var layer = doc.Layers[0] with { Transform = new(10, 15, 480, 250, rotation, true), Opacity = .7, Blend = BlendMode.Multiply };
        doc = doc.Replace(layer);
        if (rotatedOverlay) doc = doc with { Layers = doc.Layers.Add(Layer.Blank("Rotated overlay", 128, 128) with { Pixels = LayerMask.Solid(128, 128, 180).Pixels, Transform = new(100, 180, 200, 100, 17), Opacity = .4 }) };
        using var viewport = new ViewportRenderer(); int width = (int)(600 * scale), height = (int)(400 * scale);
        int compareIndex = 0;
        void Compare(Document d)
        {
            using var image = viewport.Render(d, width, height, scale, 3 * scale, 7 * scale);
            using var surface = SKSurface.Create(CanvasRenderer.Info(width, height)); surface.Canvas.Clear();
            surface.Canvas.Translate(3 * scale, 7 * scale); surface.Canvas.Scale(scale);
            using var renderer = new CanvasRenderer(); renderer.Draw(surface.Canvas, d); using var expected = surface.Snapshot();
            using var actualPixels = new SKBitmap(CanvasRenderer.Info(width, height)); using var expectedPixels = new SKBitmap(actualPixels.Info);
            image.ReadPixels(actualPixels.Info, actualPixels.GetPixels(), actualPixels.RowBytes, 0, 0);
            expected.ReadPixels(expectedPixels.Info, expectedPixels.GetPixels(), expectedPixels.RowBytes, 0, 0);
            var a = actualPixels.GetPixelSpan(); var b = expectedPixels.GetPixelSpan();
            int differences = 0, first = -1, maximum = 0;
            for (int p = 0; p < a.Length; p++) if (a[p] != b[p]) { differences++; if (first < 0) first = p; maximum = Math.Max(maximum, Math.Abs(a[p] - b[p])); }
            Assert.True(differences == 0, $"Compare {compareIndex}, differences {differences}, first {first / 4 % width},{first / 4 / width}, max {maximum}"); compareIndex++;
        }
        Compare(doc); var original = doc;
        var stroke = new MaskStroke(layer, new(100, .5, .8, 0, 0, 0), doc.Width, doc.Height);
        for (int i = 0; i < 6; i++) { stroke.Append(layer.Transform.ToDocument(new(180 + i * 30, 150), 520, 300)); doc = doc.Replace(layer with { Mask = stroke.Mask }); Compare(doc); }
        Compare(original); Compare(doc);
        var disabled = doc.Replace(doc.Layers[0] with { Mask = stroke.Mask with { Enabled = false } }); Compare(disabled); Compare(doc);
        var moved = doc.Replace(doc.Layers[0] with { Transform = layer.Transform with { X = 30, Rotation = 45, FlipY = true } }); Compare(moved);
        Compare(doc.Replace(doc.Layers[0] with { Mask = null })); Compare(doc);
    }

    [Fact]
    public void MaskSaveUsesGrayPngAndRoundTripsDisabledPixelsAndExport()
    {
        var doc = Sample(); var stroke = new MaskStroke(doc.Layers[0], new(100, .5, 1, 0, 0, 0), doc.Width, doc.Height);
        stroke.Append(new(256, 150)); doc = doc.Replace(doc.Layers[0] with { Mask = stroke.Mask });
        string path = Path.Combine(root, "Mask.comp"); ProjectStore.Save(doc, doc.Layers[0].Id, path);
        byte[] encoded = File.ReadAllBytes(Directory.GetFiles(Path.Combine(path, "images"), "*.mask.png").Single());
        Assert.Equal((byte)8, encoded[24]); Assert.Equal((byte)0, encoded[25]);
        var loaded = ProjectStore.Load(path); Assert.Equal(doc.Layers[0].Mask!.Pixels.ToRgba(), loaded.Document.Layers[0].Mask!.Pixels.ToRgba());
        Assert.Equal(Render(doc), Render(loaded.Document));
        string png = Path.Combine(root, "Masked.png"); ImageCodec.Export(doc, png, false); Assert.Equal(Render(doc), ImageCodec.Load(png).ToRgba());
        var hidden = doc.Replace(doc.Layers[0] with { Mask = LayerMask.Solid(1, 1, 0) });
        string jpg = Path.Combine(root, "Hidden.jpg"); ImageCodec.Export(hidden, jpg, true);
        Assert.All(ImageCodec.Load(jpg).ToRgba(), b => Assert.InRange(b, (byte)254, (byte)255));
        doc = doc.Replace(doc.Layers[0] with { Mask = stroke.Mask with { Enabled = false } }); ProjectStore.Save(doc, null, path);
        Assert.False(ProjectStore.Load(path).Document.Layers[0].Mask!.Enabled);
        byte[] old = File.ReadAllBytes(Path.Combine(path, "manifest.json"));
        Assert.Throws<IOException>(() => ProjectStore.SaveInternal(hidden, null, path, () => throw new IOException("failure")));
        Assert.Equal(old, File.ReadAllBytes(Path.Combine(path, "manifest.json")));
        ProjectStore.Save(doc.Replace(doc.Layers[0] with { Mask = null }), null, path);
        Assert.Empty(Directory.GetFiles(Path.Combine(path, "images"), "*.mask.png"));
    }

    [Fact]
    public void EmptyTransformedLayerKeepsMaskAndSourceDimensions()
    {
        var doc = Document.Create(320, 240); doc = doc.Replace(doc.Layers[0] with { Mask = LayerMask.Solid(320, 240), Transform = new(5, 6, 600, 400) });
        string path = Path.Combine(root, "Empty.comp"); ProjectStore.Save(doc, null, path);
        var loaded = ProjectStore.Load(path).Document; Assert.Equal(320, loaded.Layers[0].Pixels.Width); Assert.Equal(240, loaded.Layers[0].Mask!.Pixels.Height);
    }

    [Theory]
    [InlineData("path")][InlineData("missing")][InlineData("color")][InlineData("version")][InlineData("dimensions")]
    public void InvalidMasksAreRejectedBeforeReplacingExistingProject(string failure)
    {
        var doc = Sample(16, 16); string path = Path.Combine(root, "Invalid.comp"); ProjectStore.Save(doc, null, path);
        string manifest = Path.Combine(path, "manifest.json"), asset = Directory.GetFiles(Path.Combine(path, "images"), "*.mask.png").Single();
        var json = JsonNode.Parse(File.ReadAllText(manifest))!;
        switch (failure)
        {
            case "path": json["layers"]![0]!["maskFile"] = "../mask.png"; break;
            case "missing": File.Delete(asset); break;
            case "color": ImageCodec.SaveRaster(doc.Layers[0].Pixels, asset); break;
            case "version": json["version"] = 3; break;
            case "dimensions": MaskCodec.Save(LayerMask.Solid(3, 5), asset); break;
        }
        File.WriteAllText(manifest, json.ToJsonString()); var before = File.ReadAllBytes(manifest);
        if (failure == "dimensions")
        {
            Assert.Throws<NotSupportedException>(() => ProjectStore.Load(path)); Assert.Throws<NotSupportedException>(() => ProjectStore.Save(doc, null, path));
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => ProjectStore.Load(path)); Assert.Throws<InvalidDataException>(() => ProjectStore.Save(doc, null, path));
        }
        Assert.Equal(before, File.ReadAllBytes(manifest));
    }

    [Theory]
    [InlineData("maskLinked")][InlineData("maskPlacement")][InlineData("maskSourceID")]
    public void UnsupportedMaskSemanticsAreRejected(string field)
    {
        var doc = Sample(16, 16); string path = Path.Combine(root, "Unsupported.comp"); ProjectStore.Save(doc, null, path);
        string manifest = Path.Combine(path, "manifest.json"); var json = JsonNode.Parse(File.ReadAllText(manifest))!;
        json["layers"]![0]![field] = field == "maskLinked" ? JsonValue.Create(false) : new JsonObject();
        File.WriteAllText(manifest, json.ToJsonString()); Assert.Throws<NotSupportedException>(() => ProjectStore.Load(path));
    }

    [Fact]
    public void LegacyVersionFourDefaultsToEnabledLinkedMask()
    {
        var doc = Sample(16, 16); string path = Path.Combine(root, "Legacy.comp"); ProjectStore.Save(doc, null, path);
        string manifest = Path.Combine(path, "manifest.json"); var json = JsonNode.Parse(File.ReadAllText(manifest))!;
        json["version"] = 4; var layer = json["layers"]![0]!.AsObject(); layer.Remove("maskEnabled"); layer.Remove("maskLinked");
        File.WriteAllText(manifest, json.ToJsonString()); Assert.True(ProjectStore.Load(path).Document.Layers[0].Mask!.Enabled);
        layer["maskFile"] = null; layer["maskEnabled"] = false; File.WriteAllText(manifest, json.ToJsonString());
        Assert.Throws<InvalidDataException>(() => ProjectStore.Load(path));
    }
}
