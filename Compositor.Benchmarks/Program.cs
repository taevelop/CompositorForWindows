using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;

if (args.Length > 0 && args[0].StartsWith("--save-crash", StringComparison.Ordinal))
{
    SaveCrashValidation.Run(args);
    return;
}
if (args.Length > 0 && args[0] == "--soak")
{
    MvpSoakValidation.Run(args.Length > 1 ? args[1] : "soak-validation.json",
        args.Length > 2 ? int.Parse(args[2]) : 1200);
    return;
}

if (args.Length > 0 && args[0] == "--gaussian") { GaussianBlurBenchmark.Run(args.Length > 1 ? args[1] : "gaussian-benchmark.json"); return; }
if (args.Length > 0 && args[0] == "--motion-blur") { MotionBlurBenchmark.Run(args.Length > 1 ? args[1] : "motion-blur-benchmark.json"); return; }
if (args.Length > 0 && args[0] == "--lens") { LensBenchmark.Run(args.Length > 1 ? args[1] : "lens-benchmark.json"); return; }
if (args.Length > 0 && args[0] == "--add-noise") { AddNoiseBenchmark.Run(args.Length > 1 ? args[1] : "add-noise-benchmark.json"); return; }
if (args.Length > 0 && args[0] == "--fill") { FillBenchmark.Run(args.Length > 1 ? args[1] : "fill-benchmark.json"); return; }
if (args.Length > 0 && args[0] == "--gradient") { GradientBenchmark.Run(args.Length > 1 ? args[1] : "gradient-benchmark.json"); return; }
if (args.Length > 0 && args[0] == "--eyedropper") { EyedropperBenchmark.Run(args.Length > 1 ? args[1] : "eyedropper-benchmark.json"); return; }
if (args.Length > 0 && args[0] == "--pixel-adjustments") { PixelAdjustmentBenchmark.Run(args.Length > 1 ? args[1] : "pixel-adjustments-benchmark.json"); return; }
if (args.Length > 0 && args[0] == "--hue-layers") { HueLayerBenchmark.Run(args.Length > 1 ? args[1] : "hue-layers-benchmark.json"); return; }
if (args.Length > 0 && args[0] == "--hue-cube") { HueSaturationBenchmark.Run(args.Length > 1 ? args[1] : "hue-cube-benchmark.json"); return; }
if (args.Length > 0 && args[0] == "--gradient-map") { GradientMapBenchmark.Run(args.Length > 1 ? args[1] : "gradient-map-benchmark.json"); return; }
if (args.Length > 0 && args[0] == "--color-grain") { ColorBalanceGrainBenchmark.Run(args.Length > 1 ? args[1] : "color-grain-benchmark.json"); return; }
if (args.Length > 0 && args[0] == "--blackwhite") { BlackWhiteBenchmark.Run(args.Length > 1 ? args[1] : "blackwhite-benchmark.json"); return; }
if (args.Length > 0 && args[0] == "--soft-effects") { SoftEffectBenchmark.Run(args.Length > 1 ? args[1] : "soft-effects-benchmark.json"); return; }
if (args.Length > 0 && args[0] == "--stroke") { StrokeBenchmark.Run(args.Length > 1 ? args[1] : "stroke-benchmark.json"); return; }
if (args.Length > 0 && args[0] == "--shadow") { ShadowBenchmark.Run(args.Length > 1 ? args[1] : "shadow-benchmark.json"); return; }
if (args.Length > 0 && args[0] == "--overlay")
{
    ColorOverlayBenchmark.Run(args.Length > 1 ? args[1] : "overlay-benchmark.json");
    return;
}
if (args.Length > 0 && args[0] == "--curves")
{
    CurvesBenchmark.Run(args.Length > 1 ? args[1] : "curves-benchmark.json");
    return;
}
if (args.Length > 0 && args[0] == "--levels")
{
    LevelsBenchmark.Run(args.Length > 1 ? args[1] : "levels-benchmark.json");
    return;
}
if (args.Length > 0 && args[0] == "--exposure")
{
    ExposureBenchmark.Run(args.Length > 1 ? args[1] : "exposure-benchmark.json");
    return;
}

if (args.Length > 0 && args[0] == "--adjustments")
{
    AdjustmentBenchmark.Run(args.Length > 1 ? args[1] : "adjustment-benchmark.json");
    return;
}

if (args.Length > 0 && args[0] == "--groups")
{
    StabilityBenchmark.RunGroups(args.Length > 1 ? args[1] : "group-benchmark.json");
    return;
}

if (args.Length > 0 && args[0] == "--masks")
{
    StabilityBenchmark.RunMasks(args.Length > 1 ? args[1] : "mask-benchmark.json");
    return;
}

if (args.Length > 0 && args[0] == "--stability")
{
    StabilityBenchmark.Run(args.Length > 1 ? args[1] : "stability-benchmark.json");
    return;
}

string output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("benchmark.json");
var runs = new List<object>();
foreach (bool opaque in new[] { false, true })
{
    var doc = Document.Create(4000, 4000);
    if (opaque)
    {
        var bytes = new byte[4000 * 4000 * 4];
        for (int p = 0; p < bytes.Length; p += 4) { bytes[p] = 45; bytes[p + 1] = 60; bytes[p + 2] = 80; bytes[p + 3] = 255; }
        doc = doc.Replace(doc.Layers[0] with { Pixels = Raster.FromRgba(4000, 4000, bytes) });
    }
    var session = new EditorSession(doc);
    using var renderer = new ViewportRenderer(); using var surface = SKSurface.Create(CanvasRenderer.Info(1000, 1000));
    void Display() { using var frame = renderer.Render(session.Document, 1000, 1000, .25f, 0, 0); surface.Canvas.Clear(SKColors.Transparent); surface.Canvas.DrawImage(frame, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest)); surface.Canvas.Flush(); }
    Display();
    // Warm the native library and JIT before recording.
    var warm = new BrushStroke(Layer.Blank("Warm", 32, 32), new(12, .5, 1, 255, 0, 0), 32, 32); warm.Append(new(16, 16));
    for (int pass = 0; pass < 2; pass++)
    {
        var layer = session.Document.Layers[0]; var stroke = new BrushStroke(layer, new(800, 0, 1, 98, 201, 181), 4000, 4000);
        List<double> updates = [], rendering = []; long allocated = GC.GetTotalAllocatedBytes(true); var clock = new Stopwatch(); session.Begin();
        for (int i = 0; i < 120; i++)
        {
            // The entire path stays in the document; each update advances 24 pixels.
            var point = new PointD(500 + i * 24, 1100 + pass * 1200 + Math.Sin(i * .05) * 250);
            clock.Restart(); stroke.Append(point); session.Preview(session.Document.Replace(layer with { Pixels = stroke.Pixels }));
            double model = clock.Elapsed.TotalMilliseconds;
            clock.Restart(); Display(); rendering.Add(clock.Elapsed.TotalMilliseconds); updates.Add(model + rendering[^1]);
        }
        clock.Restart(); session.Commit(); Display(); double commit = clock.Elapsed.TotalMilliseconds;
        updates.Sort(); rendering.Sort();
        runs.Add(new { opaque, pass, medianUpdateMs = updates[60], p95UpdateMs = updates[113], medianRenderMs = rendering[60],
            commitMs = commit, allocatedMiB = (GC.GetTotalAllocatedBytes(true) - allocated) / 1048576.0,
            workingSetMiB = Process.GetCurrentProcess().WorkingSet64 / 1048576.0, tileCount = stroke.Pixels.Tiles.Count,
            meetsInitialBudget = updates[113] <= 33.3 && commit <= 100 });
    }
    ImageCodec.Export(session.Document, Path.ChangeExtension(output, opaque ? ".opaque.png" : ".blank.png"), false);
}
File.WriteAllText(output, JsonSerializer.Serialize(new { timeUtc = DateTimeOffset.UtcNow, os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(), processors = Environment.ProcessorCount,
    description = "Release CPU model plus offscreen Skia 1000x1000 render; 4000x4000 document, 800px soft brush, 120 updates/pass. Not WPF presentation or input-to-photon latency.", runs }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(File.ReadAllText(output));

