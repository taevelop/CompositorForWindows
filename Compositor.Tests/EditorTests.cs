using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

public sealed class EditorTests : IDisposable
{
    private readonly string temporary = Path.Combine(Path.GetTempPath(), "CompositorTests-" + Guid.NewGuid().ToString("N"));
    public EditorTests() => Directory.CreateDirectory(temporary);
    public void Dispose()
    {
        string root = Path.GetFullPath(temporary);
        if (!root.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("CompositorTests-"))
            throw new IOException("Unsafe test cleanup path.");
        Directory.Delete(root, true);
    }
    private static Raster Solid(int width, int height, byte red, byte green, byte blue, byte alpha = 255)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = red; pixels[i + 1] = green; pixels[i + 2] = blue; pixels[i + 3] = alpha; }
        return Raster.FromRgba(width, height, pixels);
    }
    private static Document Sample()
    {
        var bottom = new Layer(Guid.NewGuid(), "Bottom", Solid(32, 24, 200, 100, 50), new(0, 0, 32, 24));
        var top = new Layer(Guid.NewGuid(), "Top", Solid(9, 7, 0, 128, 0, 128), new(3, 5, 18, 14, 12), Opacity: .6, Blend: BlendMode.Multiply);
        return new(Guid.NewGuid(), 32, 24, 144, [bottom, top]);
    }
    private static byte[] Render(Document doc)
    {
        using var renderer = new CanvasRenderer(); using var image = renderer.Flatten(doc);
        using var pixels = new SKBitmap(CanvasRenderer.Info(doc.Width, doc.Height));
        Assert.True(image.ReadPixels(pixels.Info, pixels.GetPixels(), pixels.RowBytes, 0, 0));
        var result = new byte[doc.Width * doc.Height * 4]; Marshal.Copy(pixels.GetPixels(), result, 0, result.Length); return result;
    }
    [Fact] public void NativeOriginalKernelsExecuteThroughFixedWidthAbi()
    {
        Assert.Equal(1, NativePixels.AbiVersion());
        byte[] pixels = [255, 22, 17, 80, 0, 0, 0, 0]; NativePixels.Clamp(pixels, 2);
        Assert.Equal(new byte[] { 80, 22, 17, 80, 0, 0, 0, 0 }, pixels);
        int[] bounds = new int[4]; NativePixels.AlphaBounds(pixels, 2, 1, 8, bounds);
        Assert.Equal(new[] { 0, 0, 1, 1 }, bounds);
    }
    [Fact] public void TileCopiesCallerMemoryAndRasterRoundTripsEdges()
    {
        var data = new byte[PixelTile.ByteCount]; data[3] = 255;
        var tile = new PixelTile(data); data[3] = 0; Assert.Equal(255, tile.Bytes[3]);
        var raster = Solid(257, 259, 21, 31, 41); var bytes = raster.ToRgba();
        Assert.Equal(4, raster.Tiles.Count); Assert.Equal(41, bytes[^2]); Assert.Equal(255, bytes[^1]);
    }
    [Fact] public void EmptyCanvasAllocatesNoPixelTiles() => Assert.Empty(Document.Create(4000, 4000).Layers[0].Pixels.Tiles);
    [Fact] public void TransformRoundTripsRotationScaleAndFlips()
    {
        var t = new LayerTransform(25, -17, 923, 517, 39, true, true);
        var p = t.ToPixels(t.ToDocument(new(61, 89), 333, 777), 333, 777);
        Assert.Equal(61, p.X, 8); Assert.Equal(89, p.Y, 8);
    }
    [Fact] public void BrushCrossesTileBoundariesWithoutChangingEarlierSnapshot()
    {
        var layer = Layer.Blank("Paint", 600, 500);
        var stroke = new BrushStroke(layer, new(80, .5, .5, 255, 0, 0), 600, 500);
        stroke.Append(new(210, 250)); var first = stroke.Pixels; byte[] firstBytes = first.ToRgba();
        stroke.Append(new(340, 250)); byte[] result = stroke.Pixels.ToRgba();
        Assert.Equal(firstBytes, first.ToRgba()); Assert.Empty(layer.Pixels.Tiles);
        Assert.Equal(result[(250 * 600 + 255) * 4 + 3], result[(250 * 600 + 256) * 4 + 3]);
        Assert.InRange(result[(250 * 600 + 256) * 4 + 3], (byte)127, (byte)128);
    }
    [Fact] public void SparseAndDenseStraightPointerPathsHaveSamePixels()
    {
        var l = Layer.Blank("Brush", 600, 120); var settings = new BrushSettings(50, .3, .4, 120, 30, 250);
        var sparse = new BrushStroke(l, settings, 600, 120); sparse.Append(new(40, 60)); sparse.Append(new(550, 60));
        var dense = new BrushStroke(l, settings, 600, 120);
        for (int x = 40; x <= 550; x += 5) dense.Append(new(x, 60));
        Assert.Equal(sparse.Pixels.ToRgba(), dense.Pixels.ToRgba());
        byte[] prior = dense.Pixels.ToRgba(); dense.Append(new(550, 60)); Assert.Equal(prior, dense.Pixels.ToRgba());
    }
    [Fact] public void EraserKeepsPremultipliedAlphaAndOpacityCap()
    {
        var l = new Layer(Guid.NewGuid(), "Paint", Solid(32, 32, 100, 50, 25, 128), new(0, 0, 32, 32));
        var stroke = new BrushStroke(l, new(20, 1, .5, 0, 0, 0, true), 32, 32);
        stroke.Append(new(16, 16)); stroke.Append(new(16, 16));
        var p = stroke.Pixels.ToRgba().AsSpan((16 * 32 + 16) * 4, 4).ToArray();
        Assert.Equal(new byte[] { 50, 25, 13, 64 }, p);
    }
    [Fact] public void BrushUsesDocumentCoordinatesOnTransformedLayer()
    {
        var l = Layer.Blank("Paint", 100, 100) with { Transform = new(50, 20, 200, 200, 90, true) };
        var stroke = new BrushStroke(l, new(20, 1, 1, 255, 0, 0), 400, 400);
        stroke.Append(l.Transform.ToDocument(new(25.5, 70.5), 100, 100));
        Assert.Equal(255, stroke.Pixels.ToRgba()[(70 * 100 + 25) * 4 + 3]);
    }
    [Fact] public void UndoRedoRestoresImmutableImagesAndSavedRevision()
    {
        var doc = Sample(); var s = new EditorSession(doc); s.MarkSaved(); s.Begin();
        for (int i = 0; i < 10; i++) s.Preview(doc.Replace(doc.Layers[0] with { Name = "Changed " + i }));
        s.Commit(); Assert.True(s.IsModified); s.Undo(); Assert.Same(doc, s.Document); Assert.False(s.IsModified); Assert.False(s.CanUndo);
        s.Redo(); Assert.Equal("Changed 9", s.Document.Layers[0].Name); Assert.True(s.IsModified);
        s.Undo(); s.Apply(d => d.Replace(d.Layers[0] with { Visible = false })); Assert.False(s.CanRedo);
    }
    [Fact] public void InvalidEditRollsBackWithoutHistoryEntry()
    {
        var doc = Sample(); var s = new EditorSession(doc);
        Assert.Throws<InvalidDataException>(() => s.Apply(d => d.Replace(d.Layers[0] with { Opacity = 2 })));
        Assert.Same(doc, s.Document); Assert.False(s.CanUndo); Assert.False(s.InTransaction);
    }
    [Theory]
    [InlineData(BlendMode.Normal, 100, 50, 200)]
    [InlineData(BlendMode.Multiply, 78, 20, 39)]
    [InlineData(BlendMode.Screen, 222, 130, 211)]
    [InlineData(BlendMode.Difference, 100, 50, 150)]
    public void KnownOpaqueBlendPixels(BlendMode mode, int r, int g, int b)
    {
        var doc = new Document(Guid.NewGuid(), 1, 1, 72,
            [new(Guid.NewGuid(), "Bottom", Solid(1, 1, 200, 100, 50), new(0, 0, 1, 1)),
             new(Guid.NewGuid(), "Top", Solid(1, 1, 100, 50, 200), new(0, 0, 1, 1), Blend: mode)]);
        var actual = Render(doc);
        Assert.InRange(actual[0], r - 1, r + 1); Assert.InRange(actual[1], g - 1, g + 1); Assert.InRange(actual[2], b - 1, b + 1);
    }
    [Fact] public void TranslucentTileEdgesDoNotProduceSeams()
    {
        var doc = new Document(Guid.NewGuid(), 520, 8, 72,
            [new(Guid.NewGuid(), "Tiles", Solid(260, 4, 128, 0, 0, 128), new(0, 0, 520, 8), Opacity: .5)]);
        var pixels = Render(doc);
        for (int x = 506; x < 518; x++) Assert.InRange(pixels[(4 * 520 + x) * 4 + 3], (byte)63, (byte)65);
    }
    [Fact] public void SaveReopenPreservesPixelsTransformsAndActiveLayer()
    {
        var doc = Sample(); string path = Path.Combine(temporary, "Example.comp");
        ProjectStore.Save(doc, doc.Layers[1].Id, path); var opened = ProjectStore.Load(path);
        Assert.Equal(doc.Id, opened.Document.Id); Assert.Equal(144, opened.Document.Resolution);
        Assert.Equal(doc.Layers[1].Transform, opened.Document.Layers[1].Transform); Assert.Equal(doc.Layers[1].Id, opened.ActiveLayerId);
        Assert.Equal(Render(doc), Render(opened.Document));
        Assert.Equal(doc.Layers[0].Pixels.ToRgba(), opened.Document.Layers[0].Pixels.ToRgba());
        var moved = Path.Combine(temporary, "Moved.comp"); Directory.Move(path, moved); Assert.Equal(doc.Id, ProjectStore.Load(moved).Document.Id);
    }
    [Fact] public void SaveFailureAfterBackupRenameRestoresOriginal()
    {
        var doc = Sample(); string path = Path.Combine(temporary, "Safe.comp"); ProjectStore.Save(doc, null, path);
        byte[] prior = File.ReadAllBytes(Path.Combine(path, "manifest.json"));
        var changed = doc.Replace(doc.Layers[0] with { Name = "Changed" });
        Assert.Throws<IOException>(() => ProjectStore.SaveInternal(changed, null, path, () => throw new IOException("Simulated publish failure")));
        Assert.Equal(prior, File.ReadAllBytes(Path.Combine(path, "manifest.json"))); Assert.False(Directory.Exists(path + ".recovery"));
        Assert.Empty(Directory.GetDirectories(temporary, "*.staging-*"));
    }
    [Fact] public void OverwriteRemovesStaleAssets()
    {
        var doc = Sample(); string path = Path.Combine(temporary, "Overwrite.comp"); ProjectStore.Save(doc, null, path);
        ProjectStore.Save(doc with { Layers = [doc.Layers[0]] }, null, path);
        Assert.Single(Directory.GetFiles(Path.Combine(path, "images"))); Assert.Single(ProjectStore.Load(path).Document.Layers);
    }
    [Theory]
    [InlineData("text")][InlineData("effects")][InlineData("maskFile")][InlineData("shape")][InlineData("adjustment")][InlineData("futureFeature")]
    public void UnsupportedMetadataIsRejectedWithoutChangingCurrentDocument(string field)
    {
        var doc = Sample(); string path = Path.Combine(temporary, "Unsupported.comp"); ProjectStore.Save(doc, null, path);
        string manifest = Path.Combine(path, "manifest.json"); var json = JsonNode.Parse(File.ReadAllText(manifest))!;
        json["layers"]![0]![field] = field == "maskFile" ? JsonValue.Create("mask.png") : new JsonObject();
        File.WriteAllText(manifest, json.ToJsonString()); byte[] before = File.ReadAllBytes(manifest);
        Assert.Throws<NotSupportedException>(() => ProjectStore.Load(path));
        Assert.Throws<NotSupportedException>(() => ProjectStore.Save(doc, null, path)); Assert.Equal(before, File.ReadAllBytes(manifest));
    }
    [Fact] public void UnsafeAssetPathAndDuplicateIdsAreRejected()
    {
        var doc = Sample(); string path = Path.Combine(temporary, "Unsafe.comp"); ProjectStore.Save(doc, null, path);
        string manifest = Path.Combine(path, "manifest.json"); var json = JsonNode.Parse(File.ReadAllText(manifest))!;
        json["layers"]![0]!["imageFile"] = "../outside.png"; File.WriteAllText(manifest, json.ToJsonString());
        Assert.Throws<InvalidDataException>(() => ProjectStore.Load(path));
        json["layers"]![0]!["imageFile"] = null; json["layers"]![1]!["id"] = json["layers"]![0]!["id"]!.DeepClone();
        json["layers"]![1]!["imageFile"] = null; File.WriteAllText(manifest, json.ToJsonString());
        Assert.Throws<InvalidDataException>(() => ProjectStore.Load(path));
    }
    [Fact] public void UnsupportedVersionsAndMissingAssetsAreRejected()
    {
        var doc = Sample(); string path = Path.Combine(temporary, "Broken.comp"); ProjectStore.Save(doc, null, path);
        string manifest = Path.Combine(path, "manifest.json"); var json = JsonNode.Parse(File.ReadAllText(manifest))!;
        json["version"] = 9; File.WriteAllText(manifest, json.ToJsonString()); Assert.Throws<NotSupportedException>(() => ProjectStore.Load(path));
        json["version"] = 8; File.WriteAllText(manifest, json.ToJsonString()); File.Delete(Directory.GetFiles(Path.Combine(path, "images"))[0]);
        Assert.Throws<InvalidDataException>(() => ProjectStore.Load(path));
    }
    [Fact] public void EmptyVersionOneMacStylePackageLoads()
    {
        string path = Path.Combine(temporary, "Legacy.comp"); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "manifest.json"), """
        {"format":"com.compositor.project","version":1,"colorSpace":"sRGB","documentID":"76E087DB-B2AD-4B66-B324-4F7619DAEDE6","width":128,"height":128,"layers":[
          {"id":"2655499A-718B-4F6F-8C74-DF0E0E4AB815","name":"Blank","isVisible":true,"transform":{"origin":[4,8],"size":[64,96],"rotation":0,"flipX":false,"flipY":false,"sampling":"High quality"}}]}
        """);
        var loaded = ProjectStore.Load(path); Assert.Equal(72, loaded.Document.Resolution); Assert.Empty(loaded.Document.Layers[0].Pixels.Tiles);
    }
    [Fact] public void PngExportMatchesRendererAndJpegFlattensToWhite()
    {
        var doc = Sample(); string png = Path.Combine(temporary, "export.png"); ImageCodec.Export(doc, png, false);
        Assert.Equal(Render(doc), ImageCodec.Load(png).ToRgba());
        string jpeg = Path.Combine(temporary, "export.jpg"); ImageCodec.Export(Document.Create(16, 16), jpeg, true, 100);
        byte[] pixels = ImageCodec.Load(jpeg).ToRgba(); Assert.All(pixels, b => Assert.InRange(b, (byte)254, (byte)255));
        Assert.Contains("pHYs", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(png)));
    }
    [Fact] public void OversizedDimensionsAndSourceBudgetsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => Document.Create(30001, 1));
        Assert.Throws<InvalidDataException>(() => Document.Create(11000, 11000));
        string path = Path.Combine(temporary, "small.png"); ImageCodec.SaveRaster(Solid(10, 10, 255, 0, 0), path);
        Assert.Throws<InvalidDataException>(() => ImageCodec.Load(path, 99));
    }
}
