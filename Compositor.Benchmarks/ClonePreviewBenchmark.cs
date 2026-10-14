using System.Diagnostics;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;

internal static class ClonePreviewBenchmark
{
    public static void Run(string report, int side)
    {
        var doc = Document.Create(side, side); var pixels = ShapeRaster.Create(new(ShapeKind.Ellipse, .8, .2, .1), side, side);
        doc = doc.Replace(doc.Layers[0] with { Pixels = pixels });
        var rows = new List<object>();
        for (int i = 0; i < 3; i++)
        {
            long allocated = GC.GetTotalAllocatedBytes(true); var clock = Stopwatch.StartNew();
            var preview = CloneStamp.Preview(doc, doc.Layers[0].Id, new(side / 2d + i, side / 2d), new(), new(800, .7, .8, 0, 0, 0), 1);
            double elapsedMs = clock.Elapsed.TotalMilliseconds;
            if (preview.Width != 800 || preview.Height != 800 || preview.ToRgba()[(400 * 800 + 400) * 4 + 3] != 204 || !ReferenceEquals(pixels, doc.Layers[0].Pixels))
                throw new InvalidOperationException("Clone preview benchmark output/source check failed.");
            rows.Add(new { run = i + 1, elapsedMs, allocatedMiB = (GC.GetTotalAllocatedBytes(true) - allocated) / 1048576d });
        }
        using var process = Process.GetCurrentProcess(); process.Refresh();
        File.WriteAllText(report, JsonSerializer.Serialize(new { side, brush = 800, rows, peakMiB = process.PeakWorkingSet64 / 1048576d,
            note = "Synthetic ellipse, three raw active preview centers; allocation includes output check, peak includes document setup. Not WPF frame latency or composite effect cost." }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
