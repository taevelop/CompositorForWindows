using System.Text.Json.Nodes;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

public sealed class LevelsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "CompositorLevels-" + Guid.NewGuid().ToString("N"));
    public LevelsTests() => Directory.CreateDirectory(root);
    public void Dispose()
    {
        if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("CompositorLevels-")) throw new IOException("Unsafe cleanup.");
        Directory.Delete(root, true);
    }
    private static readonly LevelsAdjustment Settings = new() { Channel = LevelsChannel.Green,
        RGB = new(20, 1.3, 230, 10, 240), Red = new(10, .7, 200), Green = new(0, 2, 255, 210, 30), Blue = new(30, 1, 220, 40, 190) };
    private static Document Sample()
    {
        var doc = Document.Create(4, 1);
        return doc with { Layers = [doc.Layers[0] with { Pixels = Raster.FromRgba(4, 1,
            new byte[] { 64, 128, 192, 255, 32, 64, 96, 128, 8, 16, 24, 32, 0, 0, 0, 0 }) }, Layer.LevelsLayer(4, 1) with { Levels = Settings }] };
    }
    private static byte[] Pixels(SKImage image)
    {
        using var bitmap = new SKBitmap(CanvasRenderer.Info(image.Width, image.Height));
        Assert.True(image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0)); return bitmap.GetPixelSpan().ToArray();
    }
    private static byte[] Render(Document doc) { using var renderer = new CanvasRenderer(); using var image = renderer.Flatten(doc); return Pixels(image); }

    [Theory]
    [InlineData(-1, 1, 255, 0, 255)][InlineData(255, 1, 255, 0, 255)][InlineData(20, 1, 20.9, 0, 255)]
    [InlineData(0, .09, 255, 0, 255)][InlineData(0, 10, 255, 0, 255)][InlineData(0, 1, 256, 0, 255)]
    [InlineData(0, 1, 255, -1, 255)][InlineData(0, 1, 255, 0, 256)][InlineData(double.NaN, 1, 255, 0, 255)]
    [InlineData(0, double.PositiveInfinity, 255, 0, 255)]
    public void RejectsInvalidRanges(double black, double gamma, double white, double low, double high) =>
        Assert.Throws<InvalidDataException>(() => new LevelRange(black, gamma, white, low, high).Validate());

    [Fact]
    public void EndpointsGammaAndInvertedOutputsMatchReference()
    {
        var r = new LevelRange(20, 2, 220, 200, 40); r.Validate();
        Assert.Equal(200 / 255.0, r.Apply(0)); Assert.Equal(40 / 255.0, r.Apply(1));
        Assert.Equal((200 - Math.Sqrt(.5) * 160) / 255, r.Apply(120 / 255.0), 12);
        var table = AdjustmentProcessor.Tables(Settings);
        // Deliberately independent formula and explicit channel order from the Swift implementation.
        double F(double x, LevelRange p) => (p.OutputBlack + Math.Pow(Math.Clamp((x * 255 - p.Black) / (p.White - p.Black), 0, 1), 1 / p.Gamma) * (p.OutputWhite - p.OutputBlack)) / 255;
        var ranges = new[] { Settings.Red, Settings.Green, Settings.Blue };
        for (int c = 0; c < 3; c++) for (int i = 0; i < 256; i++) Assert.Equal((float)F(F(i / 255.0, ranges[c]), Settings.RGB), table[c * 256 + i]);
        Assert.NotEqual((float)F(F(.5, Settings.RGB), Settings.Red), (float)F(F(.5, Settings.Red), Settings.RGB));
    }

    [Fact]
    public void DistinctChannelLookupsMatchNativeKernelForEveryAlphaAndByte()
    {
        var original = new byte[256 * 256 * 4];
        for (int a = 0; a < 256; a++) for (int c = 0; c < 256; c++)
        { int p = (a * 256 + c) * 4; original[p] = original[p + 1] = original[p + 2] = (byte)(a * c / 255); original[p + 3] = (byte)a; }
        var expected = original.ToArray(); NativePixels.Levels(expected, 65536, AdjustmentProcessor.Tables(Settings));
        var doc = Document.Create(256, 256); doc = doc.Replace(doc.Layers[0] with { Pixels = Raster.FromRgba(256, 256, original) });
        doc = doc with { Layers = doc.Layers.Add(Layer.LevelsLayer(256, 256) with { Levels = Settings }) };
        Assert.Equal(expected, Render(doc));
        for (int p = 3; p < original.Length; p += 4) Assert.Equal(original[p], expected[p]);
        Assert.Equal(original, doc.Layers[0].Pixels.ToRgba());
        var redOnly = doc.Replace(doc.Layers[1] with { Levels = new() { Red = new(Gamma: 2) } }); var red = Render(redOnly);
        for (int p = 0; p < original.Length; p += 4) { Assert.Equal(original[p + 1], red[p + 1]); Assert.Equal(original[p + 2], red[p + 2]); }
        Assert.Equal(original, Render(doc.Replace(doc.Layers[1] with { Levels = new() })));
    }

    [Fact]
    public void MixedAdjustmentsRespectStackOrderAndPreserveSourceAndHistory()
    {
        var doc = Sample(); var exposure = Layer.ExposureLayer(4, 1) with { Exposure = new(.7, -.02, 1.2) };
        doc = doc with { Layers = doc.Layers.Insert(1, exposure) };
        var expected = doc.Layers[0].Pixels.ToRgba(); NativePixels.Levels(expected, 4, AdjustmentProcessor.Tables(exposure.Exposure!)); NativePixels.Levels(expected, 4, AdjustmentProcessor.Tables(Settings));
        Assert.Equal(expected, Render(doc));
        var session = new EditorSession(doc); var layer = doc.Layers[2];
        session.Apply(d => d.Replace(layer with { Levels = Settings with { RGB = Settings.RGB with { } } })); Assert.Equal(0, session.UndoCount);
        session.Apply(d => LayerHierarchy.Reorder(d, layer.Id, -1)); Assert.NotEqual(expected, Render(session.Document));
        session.Undo(); Assert.Same(doc, session.Document); session.Redo(); Assert.Equal(0, session.HistoryRetainedBytes);
        Assert.Throws<InvalidDataException>(() => doc.Replace(layer with { Exposure = new() }).Validate());
        Assert.Throws<InvalidOperationException>(() => new BrushStroke(layer, new(20, 1, 1, 255, 0, 0), 4, 1));
        var maskStroke = new MaskStroke(layer with { Mask = LayerMask.Solid(1, 1) }, new(20, 1, 1, 0, 0, 0), 4, 1); maskStroke.Append(new(2, .5));
        Assert.Empty(layer.Pixels.Tiles); Assert.Equal(Render(doc with { Layers = doc.Layers.RemoveAt(2) }), Render(doc.Replace(layer with { Mask = maskStroke.Mask })));
    }

    [Theory]
    [InlineData(BlendMode.Normal)][InlineData(BlendMode.Multiply)][InlineData(BlendMode.Screen)][InlineData(BlendMode.Overlay)]
    [InlineData(BlendMode.Darken)][InlineData(BlendMode.Lighten)][InlineData(BlendMode.Difference)]
    public void GroupOpacitySoftMaskAndBlendPreserveAlpha(BlendMode blend)
    {
        var doc = Sample(); var layer = doc.Layers[1]; var group = Layer.Group("Group", 4, 1) with { Opacity = .5 };
        var grouped = doc with { Layers = [doc.Layers[0], group, layer with { ParentId = group.Id, Blend = blend, Opacity = .5, Mask = LayerMask.Solid(1, 1, 128) }] };
        var before = doc.Layers[0].Pixels.ToRgba(); var full = Render(doc.Replace(layer with { Blend = blend })); var actual = Render(grouped);
        double weight = .25 * 128 / 255;
        for (int i = 0; i < actual.Length; i++)
            if (i % 4 == 3) Assert.Equal(before[i], actual[i]);
            else Assert.InRange((int)actual[i], (int)Math.Round(before[i] * (1 - weight) + full[i] * weight) - 1, (int)Math.Round(before[i] * (1 - weight) + full[i] * weight) + 1);
        Assert.Equal(before, Render(grouped.Replace(group with { Visible = false })));
    }

    [Theory]
    [InlineData(1f)][InlineData(1.5f)][InlineData(2f)]
    public void ViewportEditUndoAndExportAgree(float scale)
    {
        var session = new EditorSession(Sample()); using var viewport = new ViewportRenderer();
        void Compare()
        {
            using var actual = viewport.Render(session.Document, 16, 8, scale, 2, 1);
            using var surface = SKSurface.Create(CanvasRenderer.Info(16, 8)); surface.Canvas.Clear(); surface.Canvas.Translate(2, 1); surface.Canvas.Scale(scale);
            using var renderer = new CanvasRenderer(); using var flat = renderer.Flatten(session.Document);
            surface.Canvas.DrawImage(flat, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest)); using var expected = surface.Snapshot(); Assert.Equal(Pixels(expected), Pixels(actual));
        }
        Compare(); session.Apply(d => d.Replace(d.Layers[1] with { Levels = new() { Blue = new(Gamma: 2) } })); Compare();
        session.Undo(); Compare(); session.Redo(); Compare();
        session.Apply(d => d.Replace(d.Layers[0] with { Pixels = ColorAdjustments.Apply(d.Layers[0].Pixels, new(20)) })); Compare();
    }

    [Fact]
    public void SaveReopenRetainsFourChannelsMaskGridAndEditableValuesAndFailedSavePreservesOriginal()
    {
        var doc = Sample(); var layer = doc.Layers[1] with { Mask = LayerMask.Solid(4, 1, 128), Transform = new(1, 0, 3, 1) }; doc = doc.Replace(layer);
        string path = Path.Combine(root, "Levels.comp"); ProjectStore.Save(doc, layer.Id, path);
        var loaded = ProjectStore.Load(path); Assert.Equal(Settings, loaded.Document.Layers[1].Levels); Assert.Equal(Render(doc), Render(loaded.Document));
        Assert.Equal(4, loaded.Document.Layers[1].Pixels.Width); Assert.Empty(loaded.Document.Layers[1].Pixels.Tiles);
        string file = Path.Combine(path, "manifest.json"); var json = JsonNode.Parse(File.ReadAllText(file))!;
        Assert.Equal("Levels", (string)json["layers"]![1]!["adjustment"]!["kind"]!); Assert.Null(json["layers"]![1]!["imageFile"]);
        json["version"] = 7; File.WriteAllText(file, json.ToJsonString()); Assert.Equal(Settings, ProjectStore.Load(path).Document.Layers[1].Levels);
        byte[] prior = File.ReadAllBytes(file); Assert.Throws<IOException>(() => ProjectStore.SaveInternal(doc, layer.Id, path, () => throw new IOException("injected"))); Assert.Equal(prior, File.ReadAllBytes(file));
        string png = Path.Combine(root, "Levels.png"); ImageCodec.Export(doc, png, false); Assert.Equal(Render(doc), ImageCodec.Load(png).ToRgba());
        var session = new EditorSession(loaded.Document); session.Apply(d => d.Replace(d.Layers[1] with { Levels = new() })); session.Undo(); Assert.Equal(Settings, session.Document.Layers[1].Levels);
    }

    [Fact]
    public void IndependentSwiftShapedLevelsFixturePreservesSelectedChannel()
    {
        string path = Path.Combine(root, "Swift.comp"); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "manifest.json"), """
            {"format":"com.compositor.project","version":7,"colorSpace":"sRGB","documentID":"73C9F270-245F-4BCC-9533-BDB45313AEE4",
             "width":2,"height":2,"layers":[{"id":"5373B900-16CE-436E-ACCA-9FD899AF8B61","name":"Levels","isVisible":true,
             "transform":{"origin":[0,0],"size":[2,2],"rotation":0,"flipX":false,"flipY":false,"sampling":"High quality"},
             "adjustment":{"kind":"Levels","hue":0,"saturation":0,"lightness":0,"colorize":false,
             "levels":{"channel":"Blue","ranges":[
               {"black":10,"white":240,"gamma":1.2,"outputBlack":5,"outputWhite":250},
               {"black":0,"white":255,"gamma":1,"outputBlack":0,"outputWhite":255},
               {"black":0,"white":255,"gamma":1,"outputBlack":0,"outputWhite":255},
               {"black":20,"white":220,"gamma":0.8,"outputBlack":200,"outputWhite":10}]},
             "curves":{"channel":"RGB","channels":[[{"x":0,"y":0},{"x":255,"y":255}],[{"x":0,"y":0},{"x":255,"y":255}],
               [{"x":0,"y":0},{"x":255,"y":255}],[{"x":0,"y":0},{"x":255,"y":255}]]},
             "exposureSettings":{"exposure":0,"offset":0,"gamma":1}}}]}
            """);
        var loaded = ProjectStore.Load(path);
        Assert.Equal(new LevelsAdjustment { Channel = LevelsChannel.Blue, RGB = new(10, 1.2, 240, 5, 250), Blue = new(20, .8, 220, 200, 10) }, loaded.Document.Layers[0].Levels);
        Assert.All(Render(loaded.Document), b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData("missing")][InlineData("count")][InlineData("gamma")][InlineData("unknown")][InlineData("channel")][InlineData("inactive")][InlineData("duplicate")]
    public void InvalidOrUnsupportedSettingsRefuseOpenAndOverwrite(string failure)
    {
        var doc = Sample(); string path = Path.Combine(root, "Rejected.comp"); ProjectStore.Save(doc, null, path);
        string file = Path.Combine(path, "manifest.json"); var json = JsonNode.Parse(File.ReadAllText(file))!; var adjustment = json["layers"]![1]!["adjustment"]!; var levels = adjustment["levels"]!;
        switch (failure)
        {
            case "missing": levels["ranges"]![0]!.AsObject().Remove("white"); break;
            case "count": levels["ranges"]!.AsArray().RemoveAt(3); break;
            case "gamma": levels["ranges"]![1]!["gamma"] = 0; break;
            case "unknown": levels["ranges"]![0]!["future"] = true; break;
            case "channel": levels["channel"] = "red"; break;
            case "inactive": adjustment["exposureSettings"] = new JsonObject { ["exposure"] = 1, ["offset"] = 0, ["gamma"] = 1 }; break;
        }
        string text = json.ToJsonString(); if (failure == "duplicate") text = text.Replace("\"channel\":\"Green\"", "\"channel\":\"Green\",\"channel\":\"Red\"");
        File.WriteAllText(file, text); byte[] prior = File.ReadAllBytes(file);
        if (failure is "unknown" or "channel" or "inactive") { Assert.Throws<NotSupportedException>(() => ProjectStore.Load(path)); Assert.Throws<NotSupportedException>(() => ProjectStore.Save(doc, null, path)); }
        else { Assert.Throws<InvalidDataException>(() => ProjectStore.Load(path)); Assert.Throws<InvalidDataException>(() => ProjectStore.Save(doc, null, path)); }
        Assert.Equal(prior, File.ReadAllBytes(file));
    }
}
