using System.Diagnostics;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;

internal static class HealingBenchmark
{
    public static void Run(string report, int count, int diameter, bool expansion = false)
    {
        var doc = Document.Create(4000, 4000);int sourceSide = expansion ? 64 : 4000;double center = expansion ? 140.5 : 2000.5;
        var layer = doc.Layers[0] with { Pixels = ShapeRaster.Create(new(ShapeKind.Rectangle, .3, .5, .7), sourceSide, sourceSide), Transform = expansion ? new(100, 100, 64, 64) : doc.Layers[0].Transform };
        var damage = new BrushStroke(layer, new(4, 1, 1, 0, 0, 0), 4000, 4000);damage.Append(new(center, center));layer = layer with { Pixels = damage.Pixels };doc = doc.Replace(layer);
        var source = layer.Pixels;long allocated = GC.GetTotalAllocatedBytes(true);var clock = Stopwatch.StartNew();double maximumMs = 0;
        var stroke = new HealingStroke(doc, layer.Id, new(diameter, .7, 1, 0, 0, 0), HealingMode.ContentAware, 123);
        for (int i = 0; i < count; i++) { long start = Stopwatch.GetTimestamp();stroke.Append(new(center + i * 8, center));maximumMs = Math.Max(maximumMs, Stopwatch.GetElapsedTime(start).TotalMilliseconds); }
        double inputMs = clock.Elapsed.TotalMilliseconds;clock.Restart();var result = stroke.Complete();double completeMs = clock.Elapsed.TotalMilliseconds;
        result.Validate();if (ReferenceEquals(result, doc) || !ReferenceEquals(source, doc.Layers[0].Pixels)) throw new InvalidOperationException("Healing benchmark changed source or did not heal.");
        using var process = Process.GetCurrentProcess();process.Refresh();
        File.WriteAllText(report, JsonSerializer.Serialize(new { count, diameter, expansion, inputMs, maximumMs, completeMs, allocatedMiB = (GC.GetTotalAllocatedBytes(true) - allocated) / 1048576d,
            peakMiB = process.PeakWorkingSet64 / 1048576d, note = "One synthetic 4K core stroke with small black defect; dense source or 64px source expansion. Source setup excluded from allocation but included in peak. Not WPF latency or natural texture quality." }, new JsonSerializerOptions { WriteIndented = true }));
    }
    public static void Natural(string sourcePath, string report, int? centerX = null, int? centerY = null)
    {
        var source = ImageCodec.Load(sourcePath);var original = source.ToRgba();int width = source.Width, height = source.Height;
        int cx = centerX ?? width / 2, cy = centerY ?? height / 2;
        if (cx < 16 || cy < 16 || cx + 16 > width || cy + 16 > height) throw new ArgumentException("Probe center must leave room for the comparison patch.");
        var mask = new byte[width * height];var bytes = (byte[])original.Clone();double damagedError = 0;
        for (int y = cy - 3; y <= cy + 3; y++) for (int x = cx - 3; x <= cx + 3; x++)
        {
            if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > 9) continue;int p = (y * width + x) * 4;mask[y * width + x] = 255;
            for (int c = 0; c < 3; c++) { damagedError += original[p + c] * original[p + c];bytes[p + c] = 0; }
        }
        var damaged = Raster.FromRgba(width, height, bytes);ImageCodec.SaveRaster(damaged, report + ".damaged.png");var rows = new List<object>();
        using var sheet = new SKBitmap(CanvasRenderer.Info(640, 160));using var canvas = new SKCanvas(sheet);canvas.Clear(new SKColor(28, 30, 34));
        void Panel(Raster raster, int column, string label)
        {
            var rgba = raster.ToRgba();using var patch = new SKBitmap(CanvasRenderer.Info(32, 32));var output = patch.GetPixelSpan();
            for (int y = 0; y < 32; y++) rgba.AsSpan(((cy - 16 + y) * width + cx - 16) * 4, 128).CopyTo(output.Slice(y * 128, 128));
            using var image = SKImage.FromBitmap(patch);canvas.DrawImage(image, new SKRect(column * 128, 28, column * 128 + 128, 156), new SKSamplingOptions(SKFilterMode.Nearest));
            using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };using var font = new SKFont { Size = 12 };canvas.DrawText(label, column * 128 + 4, 18, SKTextAlign.Left, font, ink);
        }
        Panel(source, 0, "Original");Panel(damaged, 1, "Damaged");int panel = 2;
        foreach (var mode in Enum.GetValues<HealingMode>())
        {
            var clock = Stopwatch.StartNew();var result = SpotHealing.Apply(damaged, mask, mode: mode, seed: 123);double elapsedMs = clock.Elapsed.TotalMilliseconds;var healed = result.ToRgba();double error = 0;int channels = 0;
            for (int p = 0; p < original.Length; p++)
            {
                if (mask[p / 4] == 0 && healed[p] != original[p]) throw new InvalidOperationException("Healing changed unselected natural pixels.");
                if (mask[p / 4] != 0 && p % 4 != 3) { error += Math.Pow(healed[p] - original[p], 2);channels++; }
            }
            ImageCodec.SaveRaster(result, report + "." + mode + ".png");Panel(result, panel++, mode.ToString());rows.Add(new { mode = mode.ToString(), elapsedMs, damagedRmse = Math.Sqrt(damagedError / channels), healedRmse = Math.Sqrt(error / channels) });
        }
        if (!bytes.SequenceEqual(damaged.ToRgba())) throw new InvalidOperationException("Natural source snapshot was changed.");
        using (var image = SKImage.FromBitmap(sheet)) using (var png = image.Encode(SKEncodedImageFormat.Png, 100)) using (var output = File.Create(report + ".comparison.png")) png.SaveTo(output);
        File.WriteAllText(report, JsonSerializer.Serialize(new { sourcePath, width, height, center = new { cx, cy }, seed = 123, rows,
            note = "One injected radius-3 black defect in one natural image; exact original is ground truth for this probe only. Outside-mask pixels unchanged. Does not prove general healing or Mac parity." }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
