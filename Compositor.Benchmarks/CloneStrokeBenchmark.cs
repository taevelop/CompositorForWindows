using System.Diagnostics;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;

internal static class CloneStrokeBenchmark
{
    public static void Run(string report, int count, int diameter = 40)
    {
        var doc = Document.Create(4000, 4000);
        doc = doc.Replace(doc.Layers[0] with { Pixels = ShapeRaster.Create(new(ShapeKind.Rectangle, .8, .2, .1), 64, 64), Transform = new(100, 100, 64, 64) });
        var sample = ShapeRaster.Create(new(ShapeKind.Rectangle, .8, .2, .1), 4000, 4000);
        var sourceBytes = doc.Layers[0].Pixels.ToRgba();
        long allocated = GC.GetTotalAllocatedBytes(true);var clock = Stopwatch.StartNew();
        var stroke = new CloneStroke(doc, doc.Layers[0].Id, new(diameter, .7, .5, 0, 0, 0), sample, new());
        double maximumMs = 0;int growths = 0;var previous = doc.Layers[0].Pixels;
        for (int i = 0; i < count; i++)
        {
            long started = Stopwatch.GetTimestamp();stroke.Append(new(200 + i * 8, 200));
            maximumMs = Math.Max(maximumMs, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            var current = stroke.Document.Layers[0].Pixels;
            if (current.Width != previous.Width || current.Height != previous.Height) growths++;
            previous = current;
        }
        double inputMs = clock.Elapsed.TotalMilliseconds;clock.Restart();var result = stroke.Complete();double completeMs = clock.Elapsed.TotalMilliseconds;
        result.Validate();if (!sourceBytes.SequenceEqual(doc.Layers[0].Pixels.ToRgba()) || result.Layers[0].Pixels.Width <= 64) throw new InvalidOperationException("Clone benchmark failed.");
        ImageCodec.Export(result, report + ".png", false);
        using var process = Process.GetCurrentProcess();process.Refresh();
        File.WriteAllText(report, JsonSerializer.Serialize(new { count, diameter, growths, inputMs, maximumMs, completeMs, allocatedMiB = (GC.GetTotalAllocatedBytes(true) - allocated) / 1048576d,
            peakMiB = process.PeakWorkingSet64 / 1048576d, width = result.Layers[0].Pixels.Width, height = result.Layers[0].Pixels.Height,
            note = "One synthetic 4K core stroke; source setup excluded from allocation but included in peak. Allocation includes output PNG. Not WPF latency or natural texture." }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
