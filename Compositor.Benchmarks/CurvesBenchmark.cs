using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;

internal static class CurvesBenchmark
{
    public static void Run(string output)
    {
        var doc = Document.Create(4000, 4000); var bytes = new byte[4000 * 4000 * 4];
        for (int p = 0; p < bytes.Length; p += 4) { byte a = (byte)(64 + p / 4 % 192); bytes[p] = (byte)(a / 3); bytes[p + 1] = (byte)(a / 2); bytes[p + 2] = (byte)(a / 4); bytes[p + 3] = a; }
        var source = doc.Layers[0] with { Pixels = Raster.FromRgba(4000, 4000, bytes) };
        var runs = new List<object>();
        foreach (bool masked in new[] { false, true })
        {
            var adjustment = Layer.CurvesLayer(4000, 4000) with { Curves = new() { RGB = new(new(0, 0), new(100, 160), new(255, 255)), Red = new(new(0, 10), new(255, 240)) }, Mask = masked ? LayerMask.Solid(1, 1, 128) : null };
            var session = new EditorSession(doc with { Layers = [source, adjustment] }); using var renderer = new ViewportRenderer();
            var clock = Stopwatch.StartNew(); using (var frame = renderer.Render(session.Document, 1000, 1000, .25f, 0, 0)) { } double coldMs = clock.Elapsed.TotalMilliseconds;
            var samples = new List<object>();
            for (int pass = 0; pass < 3; pass++)
            {
                long allocated = GC.GetTotalAllocatedBytes(true); clock.Restart(); session.Apply(d => d.Replace(adjustment with { Curves = new() { RGB = new(new(0, 0), new(100, 170 + pass * 10), new(255, 255)), Red = new(new(0, 10), new(255, 240)) } }));
                using (var frame = renderer.Render(session.Document, 1000, 1000, .25f, 0, 0)) { } double editMs = clock.Elapsed.TotalMilliseconds;
                clock.Restart(); using (var frame = renderer.Render(session.Document, 1000, 1000, .25f, 2, 3)) { } double panMs = clock.Elapsed.TotalMilliseconds;
                samples.Add(new { pass, editMs, cachedPanMs = panMs, allocatedMiB = (GC.GetTotalAllocatedBytes(true) - allocated) / 1048576.0,
                    workingSetMiB = Process.GetCurrentProcess().WorkingSet64 / 1048576.0, historyBytes = session.HistoryRetainedBytes });
            }
            runs.Add(new { masked, coldMs, samples });
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { timeUtc = DateTimeOffset.UtcNow, os = RuntimeInformation.OSDescription, processors = Environment.ProcessorCount,
            description = "One 4K source and live Curves, optional uniform linked mask; canonical 4K composition and 1000px CPU viewport. Three settings edits and cached pan per case. Excludes WPF presentation and 180ms debounce.", runs }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(File.ReadAllText(output));
    }
}
