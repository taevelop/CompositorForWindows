using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;
public class TextMetadataTests
{
    [Fact]
    public void CachedPixelsAndFullTextStyleRoundTripAndPixelEditsRasterize()
    {
        var doc = Document.Create(64, 32);var pixels = ShapeRaster.Create(new(ShapeKind.Rectangle, .3, .5, .7), 64, 32);
        var text = new LayerTextStyle("한글 A😀\nsecond", "Missing-Mac-PostScript-Font", 32, .2, .4, .6, TextAlignment.Right, 1.5, 40, new(64, 32));
        var layer = doc.Layers[0] with { Pixels = pixels, Text = text, Mask = LayerMask.Solid(64, 32) };doc = doc.Replace(layer);doc.Validate();
        Assert.Same(text, (layer with { Opacity = .5, Transform = layer.Transform with { Rotation = 37 } }).Text);
        Assert.Same(text, (layer with { Mask = layer.Mask! with { Enabled = false } }).Text);
        Assert.Same(text, (layer with { Pixels = pixels }).Text);Assert.Null((layer with { Pixels = new Raster(64, 32) }).Text);
        string root = Path.Combine(Path.GetTempPath(), "text-metadata-" + Guid.NewGuid().ToString("N") + ".comp");
        try
        {
            ProjectStore.Save(doc, layer.Id, root);var loaded = ProjectStore.Load(root).Document;
            Assert.Equal(text, loaded.Layers[0].Text);Assert.Equal(pixels.ToRgba(), loaded.Layers[0].Pixels.ToRgba());
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
            var node = manifest.RootElement.GetProperty("layers")[0].GetProperty("text");Assert.Equal("Right", node.GetProperty("alignment").GetString());Assert.Equal(64, node.GetProperty("boxSize")[0].GetDouble());
            var session = new EditorSession(doc);session.Apply(d => d.Replace(layer with { Pixels = new Raster(64, 32) }));Assert.Null(session.Document.Layers[0].Text);session.Undo();Assert.Equal(text, session.Document.Layers[0].Text);session.Redo();Assert.Null(session.Document.Layers[0].Text);
        }
        finally
        {
            string resolved = Path.GetFullPath(root), parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("text-metadata-")) throw new IOException("Unsafe cleanup.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }
    [Fact]
    public void MetadataRejectsUnknownDuplicateAndInvalidFields()
    {
        using var unknown = JsonDocument.Parse("{\"text\":{\"unknown\":1}}");Assert.Throws<NotSupportedException>(() => TextJson.Read(unknown.RootElement));
        using var duplicate = JsonDocument.Parse("{\"text\":{\"content\":\"A\",\"content\":\"B\"}}");Assert.Throws<InvalidDataException>(() => TextJson.Read(duplicate.RootElement));
        Assert.Throws<InvalidDataException>(() => (new LayerTextStyle() with { Content = new string('x', 100001) }).Validate());
        Assert.Throws<InvalidDataException>(() => (new LayerTextStyle() with { BoxSize = new(10001, 10000) }).Validate());
        Assert.Throws<InvalidDataException>(() => (new LayerTextStyle() with { FontSize = double.NaN }).Validate());
        Assert.Throws<InvalidDataException>(() => (new LayerTextStyle() with { Tracking = 1001 }).Validate());
        var text = new LayerTextStyle(FontName: "");text.Validate();Assert.Equal(86.4, text.LineHeight, 8);
        var doc = Document.Create(8, 8);Assert.Throws<InvalidDataException>(() => doc.Replace(doc.Layers[0] with { Text = new(), Shape = new(ShapeKind.Rectangle, 1, 0, 0) }).Validate());
    }
}
