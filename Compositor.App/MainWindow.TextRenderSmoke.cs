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
        var regular = TextFontResolver.Resolve("ArialMT");var bold = TextFontResolver.Resolve("Arial-BoldMT");
        Check(!regular.MissingFont && !bold.MissingFont && regular.Typeface.FontFamily.Source.Contains("Arial") && bold.Typeface.Weight.ToOpenTypeWeight() > regular.Typeface.Weight.ToOpenTypeWeight(), "PostScript font aliases lost installed face/weight.");
        var fullName = TextFontResolver.Resolve("Arial Bold");Check(!fullName.MissingFont && fullName.Typeface.Weight == bold.Typeface.Weight, "Full font name lost weight.");
        var faceRender = TextRasterizer.Render(new("Bold alias", "Arial-BoldMT", 32));Check(!faceRender.MissingFont && faceRender.RequestedFont == "Arial-BoldMT", "Renderer did not retain requested PostScript name.");
        var regularRender = TextRasterizer.Render(new("Bold alias", "ArialMT", 32));
        long Alpha(Raster pixels) => pixels.ToRgba().Where((_, i) => i % 4 == 3).Sum(v => (long)v);
        Check(Alpha(faceRender.Pixels) > Alpha(regularRender.Pixels) * 1.1, "Native renderer lost the resolved bold face.");
        bool collectionChecked = false;
        foreach (var face in System.Windows.Media.Fonts.SystemTypefaces)
        {
            if (!face.TryGetGlyphTypeface(out var glyph) || !glyph.FontUri.AbsolutePath.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase)) continue;
            using var fontStream = glyph.GetFontStream();var names = OpenTypeFontNames.Read(fontStream);
            var match = names.FirstOrDefault(n => n.Get(1).Concat(n.Get(16)).Any(name => glyph.FamilyNames.Values.Contains(name, StringComparer.OrdinalIgnoreCase)) && n.Get(2).Concat(n.Get(17)).Any(name => glyph.FaceNames.Values.Contains(name, StringComparer.OrdinalIgnoreCase)));
            var alias = match?.Get(6).FirstOrDefault();if (alias is null) continue;
            var resolved = TextFontResolver.Resolve(alias);Check(!resolved.MissingFont && resolved.Typeface.Weight == face.Weight && resolved.Typeface.Style == face.Style, "Collection face alias lost its style.");collectionChecked = true;break;
        }
        Check(collectionChecked, "No installed TTC face was validated.");
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
        var smallLeading = TextRasterizer.Render(new("first\nsecond", "Segoe UI", 32, Leading: 1));
        Check(smallLeading.Pixels.Height < shortLeading.Pixels.Height, "Valid overlapping leading was rejected or ignored.");
        var missing = TextRasterizer.Render(new("Fallback", "missing-mac-font-for-test", 32));Check(missing.MissingFont && missing.RequestedFont == "missing-mac-font-for-test", "Missing font was not reported.");
        var empty = TextRasterizer.Render(new("", "Segoe UI", 32));Check(empty.Pixels.Tiles.Count == 0 && empty.Pixels.Width >= 16, "Empty point text failed.");
        bool cancelled = false;try { TextRasterizer.Render(new(), new CancellationToken(true)); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "Pre-cancelled text rendered.");
        var normalSpacing = TextRasterizer.Render(new("ABCDEFG", "Segoe UI", 32));
        var spaced = TextRasterizer.Render(new("ABCDEFG", "Segoe UI", 32, Tracking: 12));
        var tight = TextRasterizer.Render(new("ABCDEFG", "Segoe UI", 32, Tracking: -4));
        Check(spaced.Pixels.Width > normalSpacing.Pixels.Width + 60 && tight.Pixels.Width < normalSpacing.Pixels.Width - 15, "Positive/negative tracking did not change advances.");
        ImageCodec.SaveRaster(spaced.Pixels, report + ".tracking.png");
        foreach (string content in new[] { "e\u0301", "👩‍💻", "한글 자간", "مرحبا" })
        {
            var complex = TextRasterizer.Render(new(content, "Segoe UI", 48, Tracking: 3));
            Check(First(complex.Pixels).X >= 0, "Spaced cluster sample has no pixels.");
        }
        var emoji = TextRasterizer.Render(new("😀 👩‍💻", "Segoe UI Emoji", 64));
        var emojiBytes = emoji.Pixels.ToRgba(); int colorPixels = 0;
        for (int p = 0; p < emojiBytes.Length; p += 4)
        {
            Check(emojiBytes[p] <= emojiBytes[p + 3] && emojiBytes[p + 1] <= emojiBytes[p + 3] && emojiBytes[p + 2] <= emojiBytes[p + 3], "Color emoji alpha is not premultiplied.");
            if (emojiBytes[p + 3] > 128 && Math.Abs(emojiBytes[p] - emojiBytes[p + 2]) > 20) colorPixels++;
        }
        Check(colorPixels > 100, "Emoji glyphs rendered in monochrome.");ImageCodec.SaveRaster(emoji.Pixels, report + ".emoji.png");
        var fallbackEmoji = TextRasterizer.Render(new("😀", "Segoe UI", 64));
        Check(fallbackEmoji.Pixels.ToRgba().Where((_, i) => i % 4 == 0).Any(v => v > 50), "Fallback emoji has no color.");
        var worker = await Task.Run(() => TextRasterizer.Render(new("ABCDEFG", "Segoe UI", 32, Tracking: 12)));
        Check(worker.Pixels.ToRgba().SequenceEqual(spaced.Pixels.ToRgba()), "MTA render differs from UI STA render.");
        bool tooLarge = false;try { TextRasterizer.Render(new(new string('W', 100), "Segoe UI", 2000, Tracking: 1000)); } catch (ArgumentException) { tooLarge = true; }
        Check(tooLarge, "Oversized point text was not rejected before raster allocation.");
        var doc = Document.Create(400, 200);var styleRoundTrip = new LayerTextStyle("cached 😀", "Segoe UI", 32, Tracking: 3);var cached = TextRasterizer.Render(styleRoundTrip);
        doc = doc.Replace(doc.Layers[0] with { Pixels = cached.Pixels, Transform = new(0, 0, cached.Pixels.Width, cached.Pixels.Height), Text = styleRoundTrip });
        ProjectStore.Save(doc, doc.Layers[0].Id, report + ".comp");var loaded = ProjectStore.Load(report + ".comp").Document;Check(loaded.Layers[0].Text == styleRoundTrip && loaded.Layers[0].Pixels.ToRgba().SequenceEqual(cached.Pixels.ToRgba()), "Rendered text project round trip failed.");
        File.WriteAllText(report, JsonSerializer.Serialize(new { passed = true, rows, colorPixels, checks = new[] { "multilingual nonempty premul", "paragraph alignment/wrap", "leading", "font aliases", "missing font flag", "empty text", "cancel", "positive/negative tracking", "color emoji and fallback", "STA/MTA identical", "oversized point guard", "cached project pixels" }, note = "Development PC DirectWrite/Direct2D text rendering. Nonempty cluster samples do not prove every language's shaping or Mac metrics. Type editing UI, full quality and mid-layout native cancellation remain pending." }));
        foreach (var tab in workspace.Documents) tab.Session.MarkSaved();Close();
    }
}
