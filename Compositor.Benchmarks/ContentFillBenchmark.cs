using System.Diagnostics;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;

internal static class ContentFillBenchmark
{
    public static void Run(string path)
    {
        var document = Document.Create(4000,4000);
        var layer = document.Layers[0] with { Pixels = ShapeRaster.Create(new(ShapeKind.Rectangle,.3,.5,.7),4000,4000) };
        document = document.Replace(layer) with { Selection = SelectionGeometry.Box(1900,1900,200,200) };
        long allocated = GC.GetTotalAllocatedBytes(true);
        var timer = Stopwatch.StartNew();
        var result = ContentAwareFill.Apply(document,layer.Id);
        double elapsedMs = timer.Elapsed.TotalMilliseconds;
        double allocatedMiB = (GC.GetTotalAllocatedBytes(true)-allocated)/1048576d;
        if (!ReferenceEquals(result,document)) throw new InvalidOperationException("Uniform fill should preserve original document reference.");
        File.WriteAllText(path,JsonSerializer.Serialize(new {timeUtc=DateTimeOffset.UtcNow,elapsedMs,allocatedMiB,workingSetMiB=Process.GetCurrentProcess().WorkingSet64/1048576d,description="One 4000x4000 constant image, 200x200 central selection; validates no-change sharing. Cumulative managed allocation and post-operation working set, not peak memory, natural-image quality or WPF latency."},new JsonSerializerOptions{WriteIndented=true}));
    }
}
