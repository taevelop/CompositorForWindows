using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;
public partial class MainWindow
{
    internal async Task TextRenderSmoke(string report)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var rows = new List<object>();
        foreach (var content in new[] { "Hello World", "한글 테스트", "مرحبا بالعالم", "Emoji 😀", "e\u0301 café", "first\nsecond" })
        {
            var style = new LayerTextStyle(content, "Segoe UI", 32, .8, .2, .1);var result = TextRasterizer.Render(style);var bytes = result.Pixels.ToRgba();
            Check(bytes.Where((_, i) => i % 4 == 3).Any(v => v != 0), "Text produced no pixels.");
            for (int p = 0; p < bytes.Length; p += 4) Check(bytes[p] <= bytes[p + 3] && bytes[p + 1] <= bytes[p + 3] && bytes[p + 2] <= bytes[p + 3], "Text is not premultiplied.");
            ImageCodec.SaveRaster(result.Pixels, report + "." + rows.Count + ".png");rows.Add(new { content, result.Pixels.Width, result.Pixels.Height, result.ResolvedFamily, result.MissingFont });
        }
        var left = TextRasterizer.Render(new("ABC", "Segoe UI", 32, BoxSize: new(240, 80)));var right = TextRasterizer.Render(new("ABC", "Segoe UI", 32, Alignment: TextAlignment.Right, BoxSize: new(240, 80)));
        (int X, int Y) First(Raster raster) { var bytes = raster.ToRgba();for (int y = 0; y < raster.Height; y++) for (int x = 0; x < raster.Width; x++) if (bytes[(y * raster.Width + x) * 4 + 3] != 0) return (x, y);return (-1, -1); }
        Check(First(right.Pixels).X > First(left.Pixels).X + 100, "Paragraph alignment failed.");
        var wrapped = TextRasterizer.Render(new("word word word word", "Segoe UI", 24, Leading: 40, BoxSize: new(100, 180)));
        var wrapBytes = wrapped.Pixels.ToRgba();int bottom = -1;for (int y = 0; y < wrapped.Pixels.Height; y++) for (int x = 0; x < wrapped.Pixels.Width; x++) if (wrapBytes[(y * wrapped.Pixels.Width + x) * 4 + 3] > 0) bottom = y;
        Check(wrapped.Pixels.Width == 100 && wrapped.Pixels.Height == 180 && bottom > First(wrapped.Pixels).Y + 40, "Paragraph did not wrap across lines.");
        var shortLeading = TextRasterizer.Render(new("first\nsecond", "Segoe UI", 32, Leading: 24));var longLeading = TextRasterizer.Render(new("first\nsecond", "Segoe UI", 32, Leading: 60));Check(longLeading.Pixels.Height > shortLeading.Pixels.Height, "Leading failed.");
        var missing = TextRasterizer.Render(new("Fallback", "missing-mac-font-for-test", 32));Check(missing.MissingFont && missing.RequestedFont == "missing-mac-font-for-test", "Missing font was not reported.");
        var empty = TextRasterizer.Render(new("", "Segoe UI", 32));Check(empty.Pixels.Tiles.Count == 0 && empty.Pixels.Width >= 16, "Empty point text failed.");
        bool cancelled = false, trackingRejected = false;try { TextRasterizer.Render(new(), new CancellationToken(true)); } catch (OperationCanceledException) { cancelled = true; }
        try { TextRasterizer.Render(new(Tracking: 1)); } catch (NotSupportedException) { trackingRejected = true; }Check(cancelled && trackingRejected, "Cancellation/tracking guard failed.");
        var doc = Document.Create(400, 200);var styleRoundTrip = new LayerTextStyle("cached", "Segoe UI", 32);var cached = TextRasterizer.Render(styleRoundTrip);
        doc = doc.Replace(doc.Layers[0] with { Pixels = cached.Pixels, Transform = new(0, 0, cached.Pixels.Width, cached.Pixels.Height), Text = styleRoundTrip });
        ProjectStore.Save(doc, doc.Layers[0].Id, report + ".comp");var loaded = ProjectStore.Load(report + ".comp").Document;Check(loaded.Layers[0].Text == styleRoundTrip && loaded.Layers[0].Pixels.ToRgba().SequenceEqual(cached.Pixels.ToRgba()), "Rendered text project round trip failed.");
        File.WriteAllText(report, JsonSerializer.Serialize(new { passed = true, rows, checks = new[] { "multilingual nonempty premul", "paragraph alignment", "leading", "missing font flag", "empty text", "cancel", "explicit tracking guard", "cached project pixels" }, note = "Development PC WPF text foundation. Nonempty samples do not prove correct shaping/emoji colors or Mac metrics. PostScript font mapping, tracking, Type editing UI and quality remain pending." }));
        foreach (var tab in workspace.Documents) tab.Session.MarkSaved();Close();
    }
}
