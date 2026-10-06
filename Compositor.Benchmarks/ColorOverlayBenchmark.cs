using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;

internal static class ColorOverlayBenchmark
{
    public static void Run(string output)
    {
        var doc = Document.Create(4000, 4000); var bytes = new byte[4000 * 4000 * 4];
        for (int p = 0; p < bytes.Length; p += 4) { byte a = (byte)(64 + p / 4 % 192); bytes[p] = (byte)(a / 3); bytes[p + 1] = (byte)(a / 2); bytes[p + 2] = (byte)(a / 4); bytes[p + 3] = a; }
        var source = doc.Layers[0] with { Pixels = Raster.FromRgba(4000, 4000, bytes) }; var runs = new List<object>();
        foreach (bool masked in new[] { false, true })
        {
            var layer = source with { Effects = new(new(.9, .2, .4, .6)), Mask = masked ? LayerMask.Solid(1, 1, 128) : null };
            var session = new EditorSession(doc.Replace(layer)); using var renderer = new ViewportRenderer(); var clock = Stopwatch.StartNew();
            using (var frame = renderer.Render(session.Document, 1000, 1000, .25f, 0, 0)) { } double coldMs = clock.Elapsed.TotalMilliseconds;
            var samples = new List<object>();
            for (int pass = 0; pass < 3; pass++)
            {
                long allocated = GC.GetTotalAllocatedBytes(true); clock.Restart(); session.Apply(d => d.Replace(layer with { Effects = new(new(.2, .7, .9, .3 + pass * .1)) }));
                using (var frame = renderer.Render(session.Document, 1000, 1000, .25f, 0, 0)) { } double editMs = clock.Elapsed.TotalMilliseconds;
                clock.Restart(); using (var frame = renderer.Render(session.Document, 1000, 1000, .25f, 2, 3)) { } double panMs = clock.Elapsed.TotalMilliseconds;
                samples.Add(new { pass, editMs, cachedPanMs = panMs, allocatedMiB = (GC.GetTotalAllocatedBytes(true) - allocated) / 1048576.0,
                    workingSetMiB = Process.GetCurrentProcess().WorkingSet64 / 1048576.0, historyBytes = session.HistoryRetainedBytes });
            }
            layer = session.ActiveLayer!; var stroke = new BrushStroke(layer, new(800, .7, .6, 80, 160, 220), 4000, 4000);
            var dabs = new List<double>(); session.Begin();
            for (int i = 0; i < 10; i++)
            {
                clock.Restart(); stroke.Append(new(1100 + i * 120, 1500 + i * 50)); session.Preview(session.Document.Replace(layer with { Pixels = stroke.Pixels }));
                using (var frame = renderer.Render(session.Document, 1000, 1000, .25f, 2, 3)) { } dabs.Add(clock.Elapsed.TotalMilliseconds);
            }
            clock.Restart(); session.Commit(); double commitMs = clock.Elapsed.TotalMilliseconds;
            runs.Add(new { masked, coldMs, samples, brush = new { diameter = 800, dabsMs = dabs, commitMs, historyBytes = session.HistoryRetainedBytes } });
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { timeUtc = DateTimeOffset.UtcNow, os = RuntimeInformation.OSDescription, processors = Environment.ProcessorCount,
            description = "4K source with Color Overlay, optional 1x1 gray mask, 1000px CPU viewport. Three effect edits, cached pan and ten 800px brush samples. Excludes WPF and 180ms debounce.", runs }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(File.ReadAllText(output));
    }
}
