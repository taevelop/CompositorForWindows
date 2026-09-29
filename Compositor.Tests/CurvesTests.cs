using System.Text.Json.Nodes;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

public sealed class CurvesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "CompositorCurves-" + Guid.NewGuid().ToString("N"));
    public CurvesTests() => Directory.CreateDirectory(root);
    public void Dispose()
    {
        if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("CompositorCurves-")) throw new IOException("Unsafe cleanup.");
        Directory.Delete(root, true);
    }
    private static readonly CurvesAdjustment Settings = new() { Channel = LevelsChannel.Blue,
        RGB = new(new(0, 10), new(70, 40), new(180, 230), new(255, 250)), Red = new(new(0, 0), new(90, 160), new(255, 255)),
        Green = new(new(0, 220), new(120, 30), new(255, 200)), Blue = new(new(0, 255), new(255, 0)) };
    private static Document Sample()
    {
        var doc = Document.Create(4, 1);
        return doc with { Layers = [doc.Layers[0] with { Pixels = Raster.FromRgba(4, 1,
            new byte[] { 64, 128, 192, 255, 32, 64, 96, 128, 8, 16, 24, 32, 0, 0, 0, 0 }) }, Layer.CurvesLayer(4, 1) with { Curves = Settings }] };
    }
    private static byte[] Pixels(SKImage image)
    {
        using var bitmap = new SKBitmap(CanvasRenderer.Info(image.Width, image.Height));
        Assert.True(image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0)); return bitmap.GetPixelSpan().ToArray();
    }
    private static byte[] Render(Document doc) { using var renderer = new CanvasRenderer(); using var image = renderer.Flatten(doc); return Pixels(image); }

    [Theory]
    [InlineData("empty")][InlineData("one")][InlineData("many")][InlineData("first")][InlineData("last")][InlineData("duplicate")]
    [InlineData("order")][InlineData("negative")][InlineData("high")][InlineData("nan")][InlineData("infinite")][InlineData("degenerate")]
    public void RejectsMalformedCurves(string kind)
    {
        CurvePoint[] points = kind switch {
            "empty" => [], "one" => [new(0, 0)], "many" => Enumerable.Range(0, 33).Select(i => new CurvePoint(i * 255.0 / 32, i)).ToArray(),
            "first" => [new(1, 0), new(255, 255)], "last" => [new(0, 0), new(254, 255)],
            "duplicate" => [new(0, 0), new(0, 30), new(255, 255)], "order" => [new(0, 0), new(200, 100), new(100, 100), new(255, 255)],
            "negative" => [new(0, -1), new(255, 255)], "high" => [new(0, 0), new(255, 256)],
            "degenerate" => [new(0, 0), new(double.Epsilon, 255), new(255, 0)],
            "nan" => [new(0, 0), new(double.NaN, 10), new(255, 255)], _ => [new(0, 0), new(255, double.PositiveInfinity)] };
        Assert.Throws<InvalidDataException>(() => new ToneCurve(points));
    }

    [Fact]
    public void InterpolationMatchesHandComputedHermiteValuesAndPreservesHandles()
    {
        var curve = new ToneCurve(new(0, 0), new(100, 50), new(255, 205));
        // Secants are 0.5 and 1; shared tangent is 2/3, endpoint tangents are secants.
        Assert.Equal(22.916666666666668, curve.Value(50), 10);
        Assert.Equal(121.04166666666667, curve.Value(177.5), 10);
        var turning = new ToneCurve(new(0, 0), new(100, 200), new(255, 0));
        Assert.Equal(125, turning.Value(50), 10); // tangent at the local maximum is zero
        var flat = new ToneCurve(new(0, 40), new(100, 40), new(255, 200));
        Assert.Equal(40, flat.Value(50), 10);
        foreach (var channel in Enum.GetValues<LevelsChannel>())
        {
            var c = Settings.Curve(channel); foreach (var p in c.Points) Assert.Equal(p.Y, c.Value(p.X), 10);
            for (int x = 0; x <= 255; x++) Assert.InRange(c.Value(x), 0, 255);
        }
        Assert.Equal(0, curve.Value(-10)); Assert.Equal(205, curve.Value(300));
        var table = AdjustmentProcessor.Tables(Settings);
        for (int c = 0; c < 3; c++) for (int i = 0; i < 256; i++) Assert.Equal((float)(Settings.RGB.Value(Settings.Curve((LevelsChannel)(c + 1)).Value(i)) / 255), table[c * 256 + i]);
        Assert.NotEqual(Settings.RGB.Value(Settings.Blue.Value(80)), Settings.Blue.Value(Settings.RGB.Value(80)));
    }

    [Fact]
    public void ImmutablePointsAndValueEqualityPreserveNoOpHistory()
    {
        CurvePoint[] points = [new(0, 0), new(128, 180), new(255, 255)]; var curve = new ToneCurve(points); points[1] = new(80, 30);
        Assert.Equal(new(128, 180), curve.Points[1]); Assert.Equal(curve, new ToneCurve(curve.Points.ToArray()));
        var session = new EditorSession(Sample()); var layer = session.Document.Layers[1];
        session.Apply(d => d.Replace(layer with { Curves = Settings with { RGB = new(Settings.RGB.Points.ToArray()) } })); Assert.Equal(0, session.UndoCount);
        session.Apply(d => d.Replace(layer with { Curves = new() { RGB = curve } })); var prior = session.Document;
        session.Begin(); session.Preview(session.Document.Replace(layer with { Curves = new() })); session.Cancel(); Assert.Same(prior, session.Document);
        session.Undo(); Assert.Equal(Settings, session.Document.Layers[1].Curves); session.Redo(); Assert.Same(prior, session.Document); Assert.Equal(0, session.HistoryRetainedBytes);
        Assert.True(new ToneCurve(new(0, 0), new(50, 50), new(255, 255)).IsIdentity);
        Assert.Throws<InvalidDataException>(() => session.Document.Replace(layer with { Levels = new() }).Validate());
        Assert.Throws<InvalidDataException>(() => session.Document.Replace(layer with { Exposure = new() }).Validate());
        Assert.Throws<InvalidDataException>(() => new CurvesAdjustment { Channel = (LevelsChannel)99 }.Validate());
    }

    [Fact]
    public void NativeKernelAndOptimizedCompositionMatchForEveryAlphaAndByte()
    {
        var original = new byte[256 * 256 * 4];
        for (int a = 0; a < 256; a++) for (int c = 0; c < 256; c++)
        { int p = (a * 256 + c) * 4; original[p] = original[p + 1] = original[p + 2] = (byte)(a * c / 255); original[p + 3] = (byte)a; }
        var expected = original.ToArray(); NativePixels.Levels(expected, 65536, AdjustmentProcessor.Tables(Settings));
        var doc = Document.Create(256, 256); doc = doc.Replace(doc.Layers[0] with { Pixels = Raster.FromRgba(256, 256, original) });
        doc = doc with { Layers = doc.Layers.Add(Layer.CurvesLayer(256, 256) with { Curves = Settings }) };
        Assert.Equal(expected, Render(doc)); for (int p = 3; p < original.Length; p += 4) Assert.Equal(original[p], expected[p]);
        Assert.Equal(original, doc.Layers[0].Pixels.ToRgba()); Assert.Equal(original, Render(doc.Replace(doc.Layers[1] with { Curves = new() })));
        var red = Render(doc.Replace(doc.Layers[1] with { Curves = new() { Red = Settings.Red } }));
        for (int p = 0; p < original.Length; p += 4) { Assert.Equal(original[p + 1], red[p + 1]); Assert.Equal(original[p + 2], red[p + 2]); }
    }

    [Theory]
    [InlineData(BlendMode.Normal)][InlineData(BlendMode.Multiply)][InlineData(BlendMode.Screen)][InlineData(BlendMode.Overlay)]
    [InlineData(BlendMode.Darken)][InlineData(BlendMode.Lighten)][InlineData(BlendMode.Difference)]
    public void MasksGroupsBlendAndSourceGuardApplyToCurves(BlendMode blend)
    {
        var doc = Sample(); var layer = doc.Layers[1]; var group = Layer.Group("Group", 4, 1) with { Opacity = .5 };
        var grouped = doc with { Layers = [doc.Layers[0], group, layer with { ParentId = group.Id, Blend = blend, Opacity = .5, Mask = LayerMask.Solid(1, 1, 128) }] };
        var before = doc.Layers[0].Pixels.ToRgba(); var full = Render(doc.Replace(layer with { Blend = blend })); var actual = Render(grouped);
        double weight = .25 * 128 / 255;
        for (int i = 0; i < actual.Length; i++)
            if (i % 4 == 3) Assert.Equal(before[i], actual[i]);
            else Assert.InRange((int)actual[i], (int)Math.Round(before[i] * (1 - weight) + full[i] * weight) - 1, (int)Math.Round(before[i] * (1 - weight) + full[i] * weight) + 1);
        Assert.Equal(before, Render(grouped.Replace(group with { Visible = false })));
        Assert.Throws<InvalidOperationException>(() => new BrushStroke(layer, new(20, 1, 1, 0, 0, 0), 4, 1));
        var stroke = new MaskStroke(layer with { Mask = LayerMask.Solid(1, 1) }, new(20, 1, 1, 0, 0, 0), 4, 1); stroke.Append(new(2, .5));
        Assert.Equal(before, Render(doc.Replace(layer with { Mask = stroke.Mask }))); Assert.Empty(layer.Pixels.Tiles);
    }

    [Theory]
    [InlineData(1f)][InlineData(1.5f)][InlineData(2f)]
    public void MixedAdjustmentsViewportUndoAndExportAgree(float scale)
    {
        var doc = Sample(); var exposure = Layer.ExposureLayer(4, 1) with { Exposure = new(.5) }; var levels = Layer.LevelsLayer(4, 1) with { Levels = new() { RGB = new(10, .8, 240) } };
        doc = doc with { Layers = doc.Layers.Insert(1, exposure).Insert(2, levels) };
        var expected = doc.Layers[0].Pixels.ToRgba(); NativePixels.Levels(expected, 4, AdjustmentProcessor.Tables(exposure.Exposure!)); NativePixels.Levels(expected, 4, AdjustmentProcessor.Tables(levels.Levels!)); NativePixels.Levels(expected, 4, AdjustmentProcessor.Tables(Settings));
        Assert.Equal(expected, Render(doc));
        var session = new EditorSession(doc); using var viewport = new ViewportRenderer();
        void Compare()
        {
            using var actual = viewport.Render(session.Document, 16, 8, scale, 2, 1);
            using var surface = SKSurface.Create(CanvasRenderer.Info(16, 8)); surface.Canvas.Clear(); surface.Canvas.Translate(2, 1); surface.Canvas.Scale(scale);
            using var renderer = new CanvasRenderer(); using var flat = renderer.Flatten(session.Document);
            surface.Canvas.DrawImage(flat, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest)); using var image = surface.Snapshot(); Assert.Equal(Pixels(image), Pixels(actual));
        }
        Compare(); session.Apply(d => d.Replace(d.Layers[3] with { Curves = new() { Blue = Settings.Blue } })); Compare(); session.Undo(); Compare(); session.Redo(); Compare();
        session.Apply(d => LayerHierarchy.Reorder(d, d.Layers[3].Id, -1)); Compare();
        session.Apply(d => d.Replace(d.Layers[0] with { Pixels = ColorAdjustments.Apply(d.Layers[0].Pixels, new(20)) })); Compare();
    }

    [Fact]
    public void RoundTripRetainsEditablePointsMaskGridAndOriginalOnSaveFailure()
    {
        var doc = Sample(); var layer = doc.Layers[1] with { Mask = LayerMask.Solid(4, 1, 128), Transform = new(1, 0, 3, 1) }; doc = doc.Replace(layer);
        string path = Path.Combine(root, "Curves.comp"); ProjectStore.Save(doc, layer.Id, path); var loaded = ProjectStore.Load(path);
        Assert.Equal(Settings, loaded.Document.Layers[1].Curves); Assert.Equal(Render(doc), Render(loaded.Document));
        Assert.Empty(loaded.Document.Layers[1].Pixels.Tiles); Assert.Equal(4, loaded.Document.Layers[1].Pixels.Width);
        string file = Path.Combine(path, "manifest.json"); var json = JsonNode.Parse(File.ReadAllText(file))!;
        Assert.Equal("Curves", (string)json["layers"]![1]!["adjustment"]!["kind"]!); Assert.Null(json["layers"]![1]!["imageFile"]);
        json["version"] = 7; File.WriteAllText(file, json.ToJsonString()); Assert.Equal(Settings, ProjectStore.Load(path).Document.Layers[1].Curves);
        byte[] prior = File.ReadAllBytes(file); Assert.Throws<IOException>(() => ProjectStore.SaveInternal(doc, layer.Id, path, () => throw new IOException("injected"))); Assert.Equal(prior, File.ReadAllBytes(file));
        string png = Path.Combine(root, "Curves.png"); ImageCodec.Export(doc, png, false); Assert.Equal(Render(doc), ImageCodec.Load(png).ToRgba());
    }

    [Theory]
    [InlineData("count")][InlineData("empty")][InlineData("missing")][InlineData("endpoint")][InlineData("order")][InlineData("unknown")]
    [InlineData("channel")][InlineData("inactive")][InlineData("duplicate")][InlineData("kind")]
    public void InvalidOrUnsupportedCurvesRefuseOpenAndOverwrite(string failure)
    {
        var doc = Sample(); string path = Path.Combine(root, "Rejected.comp"); ProjectStore.Save(doc, null, path);
        string file = Path.Combine(path, "manifest.json"); var json = JsonNode.Parse(File.ReadAllText(file))!; var adjustment = json["layers"]![1]!["adjustment"]!; var curves = adjustment["curves"]!;
        switch (failure)
        {
            case "count": curves["channels"]!.AsArray().RemoveAt(3); break;
            case "empty": curves["channels"]![0] = new JsonArray(); break;
            case "missing": curves["channels"]![0]![1]!.AsObject().Remove("y"); break;
            case "endpoint": curves["channels"]![0]![0]!["x"] = 1; break;
            case "order": curves["channels"]![0]![1]!["x"] = 0; break;
            case "unknown": curves["channels"]![0]![0]!["future"] = true; break;
            case "channel": curves["channel"] = "blue"; break;
            case "inactive": adjustment["levels"]!["ranges"]![0]!["gamma"] = 2; break;
            case "kind": adjustment["kind"] = "Gradient Map"; break;
        }
        string text = json.ToJsonString(); if (failure == "duplicate") text = text.Replace("\"channel\":\"Blue\"", "\"channel\":\"Blue\",\"channel\":\"Red\"");
        File.WriteAllText(file, text); byte[] prior = File.ReadAllBytes(file);
        if (failure is "unknown" or "channel" or "inactive" or "kind") { Assert.Throws<NotSupportedException>(() => ProjectStore.Load(path)); Assert.Throws<NotSupportedException>(() => ProjectStore.Save(doc, null, path)); }
        else { Assert.Throws<InvalidDataException>(() => ProjectStore.Load(path)); Assert.Throws<InvalidDataException>(() => ProjectStore.Save(doc, null, path)); }
        Assert.Equal(prior, File.ReadAllBytes(file));
    }

    [Fact]
    public void IndependentSwiftShapedFixtureKeepsCurvePointsAndEditorChannel()
    {
        string path = Path.Combine(root, "Swift.comp"); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "manifest.json"), """
            {"format":"com.compositor.project","version":7,"colorSpace":"sRGB","documentID":"73C9F270-245F-4BCC-9533-BDB45313AEE4",
             "width":2,"height":2,"layers":[{"id":"5373B900-16CE-436E-ACCA-9FD899AF8B61","name":"Curves","isVisible":true,
             "transform":{"origin":[0,0],"size":[2,2],"rotation":0,"flipX":false,"flipY":false,"sampling":"High quality"},
             "adjustment":{"kind":"Curves","hue":0,"saturation":0,"lightness":0,"colorize":false,
             "levels":{"channel":"RGB","ranges":[{"black":0,"gamma":1,"white":255,"outputBlack":0,"outputWhite":255},
               {"black":0,"gamma":1,"white":255,"outputBlack":0,"outputWhite":255},{"black":0,"gamma":1,"white":255,"outputBlack":0,"outputWhite":255},
               {"black":0,"gamma":1,"white":255,"outputBlack":0,"outputWhite":255}]},
             "curves":{"channel":"Red","channels":[[{"x":0,"y":0},{"x":100,"y":160},{"x":255,"y":255}],
               [{"x":0,"y":255},{"x":255,"y":0}],[{"x":0,"y":0},{"x":255,"y":255}],[{"x":0,"y":0},{"x":255,"y":255}]]}}}]}
            """);
        var loaded = ProjectStore.Load(path); Assert.Equal(new CurvesAdjustment { Channel = LevelsChannel.Red, RGB = new(new(0, 0), new(100, 160), new(255, 255)), Red = new(new(0, 255), new(255, 0)) }, loaded.Document.Layers[0].Curves);
        Assert.All(Render(loaded.Document), b => Assert.Equal(0, b));
    }
}
