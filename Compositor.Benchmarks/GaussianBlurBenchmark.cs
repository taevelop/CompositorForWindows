using System.Diagnostics;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;
internal static class GaussianBlurBenchmark
{
    public static void Run(string path)
    {
        var style=new LayerShapeStyle(ShapeKind.Rectangle,.2,.4,.7,CornerRadius:80);
        var doc=Document.Create(4000,4000);var layer=doc.Layers[0] with{Pixels=ShapeRaster.Create(style,4000,4000)};doc=doc.Replace(layer);
        var rows=new List<object>();using var process=Process.GetCurrentProcess();
        foreach(bool preview in new[]{true,false})
        {
            long allocated=GC.GetTotalAllocatedBytes(true);var clock=Stopwatch.StartNew();
            var result=preview?GaussianBlur.Preview(doc,layer.Id,10):GaussianBlur.Apply(doc,layer.Id,10);
            double elapsedMs=clock.Elapsed.TotalMilliseconds;var pixels=result.Layers[0].Pixels;
            if(preview&&Math.Max(pixels.Width,pixels.Height)>2048||!ReferenceEquals(doc.Layers[0].Pixels,layer.Pixels))throw new InvalidOperationException("Blur preview cap or source immutability failed.");
            process.Refresh();rows.Add(new{preview,elapsedMs,allocatedMiB=(GC.GetTotalAllocatedBytes(true)-allocated)/1048576d,width=pixels.Width,height=pixels.Height,workingSetMiB=process.WorkingSet64/1048576d});
        }
        File.WriteAllText(path,JsonSerializer.Serialize(new{timeUtc=DateTimeOffset.UtcNow,
            description="4000x4000 rounded opaque rectangle, Gaussian sigma 10. One 2048px preview and one full-resolution apply. Core CPU and cumulative managed allocation only; sampled working set is not peak or WPF latency.",rows},new JsonSerializerOptions{WriteIndented=true}));
    }
}
