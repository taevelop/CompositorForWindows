using System.Diagnostics;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;

internal static class LensBenchmark
{
    public static void Run(string path)
    {
        var document = Document.Create(4000, 4000);
        var layer = document.Layers[0] with { Pixels = ShapeRaster.Create(new(ShapeKind.Rectangle, .3, .5, .7), 4000, 4000) };
        document = document.Replace(layer);
        var rows = new List<object>();
        foreach (double distortion in new[] { -80d, 80d })
        {
            long start = GC.GetTotalAllocatedBytes(true);
            var timer = Stopwatch.StartNew();
            var preview = LensCorrection.Preview(document, layer.Id, distortion);
            double previewMs = timer.Elapsed.TotalMilliseconds;
            double previewAllocatedMiB = (GC.GetTotalAllocatedBytes(true) - start) / 1048576d;
            start = GC.GetTotalAllocatedBytes(true); timer.Restart();
            var full = LensCorrection.Apply(document, layer.Id, distortion);
            double applyMs = timer.Elapsed.TotalMilliseconds;
            double applyAllocatedMiB = (GC.GetTotalAllocatedBytes(true) - start) / 1048576d;
            if (preview.Layers[0].Pixels.Width != 2048 || full.Layers[0].Pixels.Width != 4000 || !ReferenceEquals(document.Layers[0].Pixels, layer.Pixels))
                throw new InvalidOperationException("Lens dimensions or source ownership changed.");
            rows.Add(new { distortion, previewMs, previewAllocatedMiB, applyMs, applyAllocatedMiB });
        }
        File.WriteAllText(path, JsonSerializer.Serialize(new { timeUtc = DateTimeOffset.UtcNow, description = "4000x4000 opaque rectangle; one core run for each sign. Cumulative managed allocations, not peak memory or WPF input latency.", rows }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
