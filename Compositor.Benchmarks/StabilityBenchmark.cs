using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;

internal static class StabilityBenchmark
{
    public static void Run(string path)
    {
        var runs = new List<object>();
        foreach (int layers in new[] { 1, 3, 6 })
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            runs.Add(MeasureLayers(layers));
        }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var history = MeasureHistory();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            timeUtc = DateTimeOffset.UtcNow, os = RuntimeInformation.OSDescription,
            processors = Environment.ProcessorCount,
            description = "Release CPU model and offscreen Skia 1000x1000 viewport. Distinct 4000x4000 sources, 800px soft brush, 120 updates per stroke. Not WPF presentation or physical input latency. Process peak includes earlier scenarios and fixture construction.",
            runs, history
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(File.ReadAllText(path));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object MeasureLayers(int count)
    {
        var document = Document.Create(4000, 4000);
        var layers = document.Layers.Clear();
        for (int i = 0; i < count; i++)
        {
            var bytes = new byte[4000 * 4000 * 4];
            for (int p = 0; p < bytes.Length; p += 4)
            {
                bytes[p] = (byte)(45 + i * 20); bytes[p + 1] = (byte)(60 + (p / 4 % 4000) / 40);
                bytes[p + 2] = (byte)(80 + i * 10); bytes[p + 3] = 255;
            }
            layers = layers.Add(Layer.Blank($"Layer {i + 1}", 4000, 4000) with
            {
                Pixels = Raster.FromRgba(4000, 4000, bytes), Opacity = i == 0 ? 1 : .6,
                Blend = i % 2 == 0 ? BlendMode.Normal : BlendMode.Multiply
            });
        }
        var session = new EditorSession(document with { Layers = layers });
        using var viewport = new ViewportRenderer();
        using var output = SKSurface.Create(CanvasRenderer.Info(1000, 1000));
        void Display()
        {
            using var frame = viewport.Render(session.Document, 1000, 1000, .25f, 0, 0);
            output.Canvas.Clear(); output.Canvas.DrawImage(frame, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest)); output.Canvas.Flush();
        }
        var clock = Stopwatch.StartNew(); Display(); double coldFrameMs = clock.Elapsed.TotalMilliseconds;
        var warm = new BrushStroke(Layer.Blank("Warm", 32, 32), new(12, .5, 1, 255, 0, 0), 32, 32); warm.Append(new(16, 16));
        var passes = new List<object>();
        for (int pass = 0; pass < 2; pass++)
        {
            // Edit the bottom layer so every overlying blend participates in each damaged region.
            var layer = session.Document.Layers[0];
            var stroke = new BrushStroke(layer, new(800, 0, 1, 98, 201, 181), 4000, 4000);
            var updates = new List<double>(); var renders = new List<double>();
            long allocated = GC.GetTotalAllocatedBytes(true); session.Begin();
            for (int i = 0; i < 120; i++)
            {
                clock.Restart(); stroke.Append(new(500 + i * 24, 1100 + pass * 1200 + Math.Sin(i * .05) * 250));
                session.Preview(session.Document.Replace(layer with { Pixels = stroke.Pixels }));
                double model = clock.Elapsed.TotalMilliseconds; clock.Restart(); Display();
                renders.Add(clock.Elapsed.TotalMilliseconds); updates.Add(model + renders[^1]);
            }
            clock.Restart(); session.Commit(); Display(); double commitMs = clock.Elapsed.TotalMilliseconds;
            updates.Sort(); renders.Sort();
            using var process = Process.GetCurrentProcess();
            passes.Add(new
            {
                pass, medianUpdateMs = updates[60], p95UpdateMs = updates[113], medianRenderMs = renders[60], commitMs,
                allocatedMiB = (GC.GetTotalAllocatedBytes(true) - allocated) / 1048576d,
                retainedManagedMiB = GC.GetTotalMemory(true) / 1048576d,
                workingSetMiB = process.WorkingSet64 / 1048576d, processPeakWorkingSetMiB = process.PeakWorkingSet64 / 1048576d,
                historyTileMiB = session.HistoryRetainedBytes / 1048576d,
                meetsInitialBudget = updates[113] <= 33.3 && commitMs <= 100
            });
        }
        return new { layers = count, sourceMegapixels = 16 * count, coldFrameMs, passes };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object MeasureHistory()
    {
        var session = new EditorSession(Document.Create(1024, 1024));
        using var renderer = new ViewportRenderer();
        var checkpoints = new List<object>();
        var commits = new List<double>();
        void Sample(string stage)
        {
            using var process = Process.GetCurrentProcess();
            checkpoints.Add(new { stage, session.UndoCount, session.RedoCount,
                currentTileMiB = session.Document.Layers.Sum(l => l.Pixels.AllocatedBytes) / 1048576d,
                historyTileMiB = session.HistoryRetainedBytes / 1048576d,
                retainedManagedMiB = GC.GetTotalMemory(true) / 1048576d, workingSetMiB = process.WorkingSet64 / 1048576d });
        }
        for (int i = 0; i < 160; i++)
        {
            ApplyStroke(session, i);
            using var frame = renderer.Render(session.Document, 512, 512, .5f, 0, 0);
            var watch = Stopwatch.StartNew(); session.Commit(); commits.Add(watch.Elapsed.TotalMilliseconds);
            if ((i + 1) % 40 == 0) Sample($"stroke-{i + 1}");
        }
        for (int i = 0; i < 20; i++) session.Undo();
        using (var frame = renderer.Render(session.Document, 512, 512, .5f, 0, 0)) { }
        Sample("undo-20");
        for (int i = 0; i < 20; i++) session.Redo();
        using (var frame = renderer.Render(session.Document, 512, 512, .5f, 0, 0)) { }
        Sample("redo-20");
        session.Load(Document.Create(1024, 1024));
        using (var frame = renderer.Render(session.Document, 512, 512, .5f, 0, 0)) { }
        Sample("new-document"); commits.Sort();
        return new { description = "160 distinct-color 800px strokes on 1024x1024, then 20 undo/redo and new document; forced-GC managed memory is diagnostic, not a leak proof.",
            p95CommitMs = commits[(int)Math.Ceiling(commits.Count * .95) - 1], checkpoints };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ApplyStroke(EditorSession session, int i)
    {
        var layer = session.Document.Layers[0];
        var stroke = new BrushStroke(layer, new(800, .5, .8, (byte)(i * 17 % 256), (byte)(i * 31 % 256), 80), 1024, 1024);
        stroke.Append(new(420 + i % 3 * 80, 420 + i % 5 * 40));
        session.Begin(); session.Preview(session.Document.Replace(layer with { Pixels = stroke.Pixels }));
    }
}
