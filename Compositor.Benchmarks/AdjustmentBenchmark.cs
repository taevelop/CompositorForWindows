using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;

internal static class AdjustmentBenchmark
{
    public static void Run(string output)
    {
        const int side = 4000;
        var bytes = new byte[side * side * 4];
        for (int y = 0; y < side; y++) for (int x = 0; x < side; x++)
        {
            int p = (y * side + x) * 4; byte a = (byte)(64 + x % 192);
            bytes[p] = (byte)(a * (x % 256) / 255); bytes[p + 1] = (byte)(a * (y % 256) / 255); bytes[p + 2] = (byte)(a / 3); bytes[p + 3] = a;
        }
        var doc = Document.Create(side, side); var layer = doc.Layers[0] with { Pixels = Raster.FromRgba(side, side, bytes), Mask = LayerMask.Solid(1, 1, 180) };
        doc = doc.Replace(layer); var session = new EditorSession(doc);
        using var viewport = new ViewportRenderer(); using var initial = viewport.Render(doc, 1000, 1000, .25f, 0, 0);
        ColorAdjustments.Apply(Raster.FromRgba(1, 1, new byte[] { 30, 60, 90, 128 }), new(10, 20, -30));
        var runs = new List<object>();
        for (int pass = 0; pass < 5; pass++)
        {
            long allocated = GC.GetTotalAllocatedBytes(true); var timer = Stopwatch.StartNew();
            var adjusted = ColorAdjustments.Apply(layer.Pixels, new(10 + pass, 20, -30)); double computeMs = timer.Elapsed.TotalMilliseconds;
            session.Begin(); timer.Restart(); session.Preview(doc.Replace(layer with { Pixels = adjusted }));
            using var frame = viewport.Render(session.Document, 1000, 1000, .25f, 0, 0); double renderMs = timer.Elapsed.TotalMilliseconds;
            timer.Restart(); session.Commit(); double commitMs = timer.Elapsed.TotalMilliseconds;
            runs.Add(new { pass, computeMs, renderMs, commitMs, totalMs = computeMs + renderMs + commitMs,
                allocatedMiB = (GC.GetTotalAllocatedBytes(true) - allocated) / 1048576.0,
                workingSetMiB = Process.GetCurrentProcess().WorkingSet64 / 1048576.0, historyMiB = session.HistoryRetainedBytes / 1048576.0 });
            session.Undo();
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new { timeUtc = DateTimeOffset.UtcNow, os = RuntimeInformation.OSDescription,
            processors = Environment.ProcessorCount, description = "4000x4000 premultiplied sRGB source and uniform mask; all three adjustments; CPU 1000x1000 Skia viewport. Five previews from the same original. Excludes 180ms debounce, dispatcher and physical presentation.", runs }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(File.ReadAllText(output));
    }
}
