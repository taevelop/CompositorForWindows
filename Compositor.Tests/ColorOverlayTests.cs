using System.Text.Json.Nodes;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

public sealed class ColorOverlayTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "CompositorOverlay-" + Guid.NewGuid().ToString("N"));
    public ColorOverlayTests() => Directory.CreateDirectory(root);
    public void Dispose()
    {
        if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("CompositorOverlay-")) throw new IOException("Unsafe cleanup.");
        Directory.Delete(root, true);
    }
    private static Document Sample()
    {
        var doc = Document.Create(4, 1); return doc.Replace(doc.Layers[0] with { Pixels = Raster.FromRgba(4, 1,
            new byte[] { 64, 128, 192, 255, 64, 32, 16, 128, 8, 16, 24, 32, 0, 0, 0, 0 }), Effects = new(new(1, .2, .4, .5)) });
    }
    private static byte[] Pixels(SKImage image)
    {
        using var bitmap = new SKBitmap(CanvasRenderer.Info(image.Width, image.Height));
        Assert.True(image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0)); return bitmap.GetPixelSpan().ToArray();
    }
    private static byte[] Render(Document doc) { using var renderer = new CanvasRenderer(); using var image = renderer.Flatten(doc); return Pixels(image); }

    [Theory]
    [InlineData(-.01, 0, 0, 1)][InlineData(0, 1.01, 0, 1)][InlineData(0, 0, -1, 1)][InlineData(0, 0, 0, 1.01)]
    [InlineData(0, 0, 0, -.01)][InlineData(double.NaN, 0, 0, 1)][InlineData(0, double.PositiveInfinity, 0, 1)]
    public void RejectsInvalidSettings(double r, double g, double b, double opacity) => Assert.Throws<InvalidDataException>(() => new ColorOverlayEffect(r, g, b, opacity).Validate());

    [Theory]
    [InlineData(0)][InlineData(.5)][InlineData(1)]
    public void SourceOverMatchesReferenceForEveryAlphaAndPreservesOriginal(double opacity)
    {
        var doc = Document.Create(256, 256); var source = new byte[256 * 256 * 4];
        for (int a = 0; a < 256; a++) for (int c = 0; c < 256; c++)
        { int p = (a * 256 + c) * 4; source[p] = (byte)(c * a / 255); source[p + 1] = (byte)(a / 2); source[p + 2] = (byte)(a / 3); source[p + 3] = (byte)a; }
        var layer = doc.Layers[0] with { Pixels = Raster.FromRgba(256, 256, source), Effects = new(new(.9, .2, .4, opacity)) }; doc = doc.Replace(layer);
        byte[] actual = Render(doc); double[] color = [.9, .2, .4, 1];
        for (int p = 0; p < source.Length; p += 4)
        {
            double coverage = source[p + 3] / 255.0 * opacity;
            // Independent decimal arithmetic; float/double operation order can differ by one byte at half steps.
            for (int c = 0; c < 4; c++)
            {
                decimal weight = source[p + 3] / 255m * (decimal)opacity;
                int expected = (int)Math.Round((decimal)color[c] * weight * 255 + source[p + c] * (1 - weight), MidpointRounding.AwayFromZero);
                Assert.InRange((int)actual[p + c], Math.Max(0, expected - 1), Math.Min(255, expected + 1));
            }
            Assert.InRange(actual[p + 3], source[p + 3], (byte)255);
            for (int c = 0; c < 3; c++) Assert.InRange(actual[p + c], (byte)0, actual[p + 3]);
        }
        Assert.Equal(source, layer.Pixels.ToRgba());
        Assert.Equal(source, Render(doc.Replace(layer with { Effects = new(layer.Effects.ColorOverlay! with { Enabled = false }) })));
        byte[] known = [64, 32, 16, 128]; ColorOverlayProcessor.Apply(known, new(1, 0, 0, .5)); Assert.Equal(new byte[] {112, 24, 12, 160}, known);
    }

    [Theory]
    [InlineData(0)][InlineData(128)][InlineData(255)]
    public void MaskIsAppliedBeforeOverlayAndLayerOpacityAfterIt(byte mask)
    {
        var doc = Sample(); var layer = doc.Layers[0] with { Mask = LayerMask.Solid(1, 1, mask) }; doc = doc.Replace(layer);
        var reference = layer.Pixels.ToRgba(); for (int i = 0; i < reference.Length; i++) reference[i] = (byte)((reference[i] * mask + 127) / 255);
        ColorOverlayProcessor.Apply(reference, layer.Effects!.ColorOverlay!); Assert.Equal(reference, Render(doc));
        var baked = doc.Replace(layer with { Pixels = Raster.FromRgba(4, 1, reference), Mask = null, Effects = null, Opacity = .4 });
        Assert.Equal(Render(baked), Render(doc.Replace(layer with { Opacity = .4 })));
        Assert.Equal(Render(Sample()), Render(doc.Replace(layer with { Mask = layer.Mask with { Enabled = false } })));
    }

    [Theory]
    [InlineData(BlendMode.Normal)][InlineData(BlendMode.Multiply)][InlineData(BlendMode.Screen)][InlineData(BlendMode.Overlay)]
    [InlineData(BlendMode.Darken)][InlineData(BlendMode.Lighten)][InlineData(BlendMode.Difference)]
    public void GroupBlendAndLaterAdjustmentUseEffectResult(BlendMode blend)
    {
        var doc = Sample(); var layer = doc.Layers[0]; var baked = Render(doc); var group = Layer.Group("Group", 4, 1) with { Opacity = .6 };
        var background = Layer.Blank("Background", 4, 1) with { Pixels = LayerMask.Solid(4, 1, 90).Pixels };
        var curves = Layer.CurvesLayer(4, 1) with { Curves = new() { RGB = new(new(0, 0), new(100, 170), new(255, 255)) } };
        layer = layer with { ParentId = group.Id, Opacity = .7, Blend = blend };
        doc = doc with { Layers = [background, group, layer, curves] };
        Assert.Equal(Render(doc.Replace(layer with { Pixels = Raster.FromRgba(4, 1, baked), Effects = null })), Render(doc));
        var session = new EditorSession(doc); session.Apply(d => LayerHierarchy.Ungroup(d, group.Id)); Assert.Equal(Render(doc), Render(session.Document));
        session.Undo(); Assert.Same(doc, session.Document);
        Assert.Throws<NotSupportedException>(() => doc.Replace(group with { Effects = new(new()) }).Validate());
        Assert.Throws<NotSupportedException>(() => doc.Replace(curves with { Effects = new(new()) }).Validate());
    }

    [Theory]
    [InlineData(1f, 0)][InlineData(1.5f, 0)][InlineData(2f, 17)]
    public void TileBoundaryPaintingMaskEditsToggleRemoveUndoAndViewportMatchFreshRender(float zoom, double angle)
    {
        var doc = Document.Create(520, 300);
        var layer = doc.Layers[0] with { Pixels = LayerMask.Solid(520, 300, 80).Pixels, Effects = new(new(.2, .7, 1, .6)), Transform = new(0, 0, 520, 300, angle), Mask = LayerMask.Solid(520, 300) };
        var session = new EditorSession(doc.Replace(layer)); using var viewport = new ViewportRenderer();
        void Compare()
        {
            using var actual = viewport.Render(session.Document, 800, 650, zoom, 10, 4); using var surface = SKSurface.Create(CanvasRenderer.Info(800, 650));
            surface.Canvas.Clear(); surface.Canvas.Translate(10, 4); surface.Canvas.Scale(zoom);
            using var fresh = new CanvasRenderer(); fresh.Draw(surface.Canvas, session.Document); using var expected = surface.Snapshot(); Assert.Equal(Pixels(expected), Pixels(actual));
            using var export = fresh.Flatten(session.Document); Assert.Equal(Render(session.Document), Pixels(export));
        }
        Compare(); session.Apply(d => d.Replace(d.Layers[0] with { Effects = new(new(1, .2, 0, .9)) })); Compare();
        session.Apply(d => d.Replace(d.Layers[0] with { Effects = new(d.Layers[0].Effects!.ColorOverlay! with { Enabled = false }) })); Compare(); session.Undo(); Compare();
        var brush = new BrushStroke(session.Document.Layers[0], new(70, .5, .7, 20, 180, 100), 520, 300); brush.Append(new(256, 150));
        session.Apply(d => d.Replace(d.Layers[0] with { Pixels = brush.Pixels })); Compare();
        var mask = new MaskStroke(session.Document.Layers[0], new(70, .5, 1, 0, 0, 0), 520, 300); mask.Append(new(256, 150));
        session.Apply(d => d.Replace(d.Layers[0] with { Mask = mask.Mask })); Compare();
        session.Apply(d => d.Replace(d.Layers[0] with { Effects = null })); Compare(); session.Undo(); Compare(); session.Redo(); Compare();
    }

    [Fact]
    public void TileGuttersMatchEquivalentBakedImageAcrossBoundary()
    {
        var doc = Document.Create(520, 8); var source = new byte[520 * 8 * 4];
        for (int i = 0; i < source.Length; i += 4) { source[i] = 32; source[i + 1] = 64; source[i + 2] = 96; source[i + 3] = 128; }
        var effect = new ColorOverlayEffect(.9, .1, .3, .8); var baked = source.ToArray(); ColorOverlayProcessor.Apply(baked, effect);
        var layer = doc.Layers[0] with { Pixels = Raster.FromRgba(520, 8, source), Effects = new(effect), Transform = new(.3, .2, 519, 7, Sampling: Sampling.Smooth) };
        Assert.Equal(Render(doc.Replace(layer with { Pixels = Raster.FromRgba(520, 8, baked), Effects = null })), Render(doc.Replace(layer)));
    }

    [Fact]
    public void SaveReopenKeepsSourceOptionalFlagMaskAndHistoryAndFailedSaveKeepsOriginal()
    {
        var doc = Sample(); var layer = doc.Layers[0] with { Mask = LayerMask.Solid(4, 1, 128) }; doc = doc.Replace(layer);
        string path = Path.Combine(root, "Overlay.comp"); ProjectStore.Save(doc, layer.Id, path); var loaded = ProjectStore.Load(path).Document;
        Assert.Equal(layer.Effects, loaded.Layers[0].Effects); Assert.Equal(layer.Pixels.ToRgba(), loaded.Layers[0].Pixels.ToRgba()); Assert.Equal(Render(doc), Render(loaded));
        var session = new EditorSession(loaded); session.Apply(d => d.Replace(d.Layers[0] with { Effects = new(layer.Effects!.ColorOverlay! with { Opacity = .8 }) }));
        Assert.Equal(0, session.HistoryRetainedBytes); session.Undo(); Assert.Equal(layer.Effects, session.ActiveLayer!.Effects); session.Redo();
        string file = Path.Combine(path, "manifest.json"); byte[] prior = File.ReadAllBytes(file);
        Assert.Throws<IOException>(() => ProjectStore.SaveInternal(session.Document, layer.Id, path, () => throw new IOException("injected"))); Assert.Equal(prior, File.ReadAllBytes(file));
        string png = Path.Combine(root, "Overlay.png"); ImageCodec.Export(doc, png, false); Assert.Equal(Render(doc), ImageCodec.Load(png).ToRgba());
        var json = JsonNode.Parse(File.ReadAllText(file))!; json["layers"]![0]!["effects"] = JsonNode.Parse("""{"stroke":null,"shadow":null,"colorOverlay":{"red":0.25,"green":0.75,"blue":1,"opacity":0.5},"innerShadow":null,"outerGlow":null}""");
        File.WriteAllText(file, json.ToJsonString()); Assert.Equal(new ColorOverlayEffect(.25, .75, 1, .5), ProjectStore.Load(path).Document.Layers[0].Effects!.ColorOverlay);
        json["layers"]![0]!["effects"]!["colorOverlay"]!["enabled"] = false; File.WriteAllText(file, json.ToJsonString()); Assert.False(ProjectStore.Load(path).Document.Layers[0].Effects!.ColorOverlay!.IsEnabled);
        json["layers"]![0]!["effects"] = new JsonObject(); File.WriteAllText(file, json.ToJsonString()); Assert.Equal(new(), ProjectStore.Load(path).Document.Layers[0].Effects);
    }

    [Theory]
    [InlineData("missing")][InlineData("range")][InlineData("flag")][InlineData("duplicate")][InlineData("unknown")]
    [InlineData("shadow")][InlineData("disabledShadow")][InlineData("group")][InlineData("adjustment")]
    public void UnsupportedOrMalformedEffectsRefuseOpenAndOverwrite(string failure)
    {
        var doc = Sample(); string path = Path.Combine(root, "Rejected.comp"); ProjectStore.Save(doc, null, path);
        string file = Path.Combine(path, "manifest.json"); var json = JsonNode.Parse(File.ReadAllText(file))!; var layer = json["layers"]![0]!; var effects = layer["effects"]!; var overlay = effects["colorOverlay"]!;
        switch (failure)
        {
            case "missing": overlay.AsObject().Remove("red"); break;
            case "range": overlay["opacity"] = 2; break;
            case "flag": overlay["enabled"] = "true"; break;
            case "unknown": overlay["future"] = true; break;
            case "shadow": effects["shadow"] = new JsonObject(); break;
            case "disabledShadow": effects["shadow"] = new JsonObject { ["enabled"] = false }; break;
            case "group": layer["isGroup"] = true; layer["imageFile"] = null; break;
            case "adjustment": layer["imageFile"] = null; layer["adjustment"] = new JsonObject { ["kind"] = "Exposure" }; break;
        }
        string text = json.ToJsonString(); if (failure == "duplicate") text = text.Replace("\"red\":1", "\"red\":1,\"red\":0");
        File.WriteAllText(file, text); byte[] prior = File.ReadAllBytes(file);
        if (failure is "missing" or "range" or "flag" or "duplicate") { Assert.Throws<InvalidDataException>(() => ProjectStore.Load(path)); Assert.Throws<InvalidDataException>(() => ProjectStore.Save(doc, null, path)); }
        else { Assert.Throws<NotSupportedException>(() => ProjectStore.Load(path)); Assert.Throws<NotSupportedException>(() => ProjectStore.Save(doc, null, path)); }
        Assert.Equal(prior, File.ReadAllBytes(file));
    }
}
