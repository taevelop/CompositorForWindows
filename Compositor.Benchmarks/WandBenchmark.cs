using System.Diagnostics;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;
internal static class WandBenchmark
{
    public static void Run(string report,int side)
    {
        var document=Document.Create(side,side);var layer=document.Layers[0] with{Pixels=ShapeRaster.Create(new(ShapeKind.Ellipse,.8,.2,.1),side,side)};document=document.Replace(layer);
        var rows=new List<object>();
        foreach(bool contiguous in new[]{true,false})
        {
            long allocated=GC.GetTotalAllocatedBytes(true);var clock=Stopwatch.StartNew();var result=MagicWand.Compute(document,layer.Id,new(side/2d,side/2d),new(32,0,contiguous));double elapsedMs=clock.Elapsed.TotalMilliseconds;
            if(result.Selection is null||!SelectionGeometry.Contains(result.Selection,new(side/2d,side/2d))||SelectionGeometry.Contains(result.Selection,new(.5,.5))||!ReferenceEquals(result.Layers[0].Pixels,layer.Pixels))throw new InvalidOperationException("Wand large selection failed geometric/source checks.");
            rows.Add(new{contiguous,elapsedMs,allocatedMiB=(GC.GetTotalAllocatedBytes(true)-allocated)/1048576d,pathCharacters=result.Selection.PathData.Length});
        }
        using var process=Process.GetCurrentProcess();process.Refresh();
        File.WriteAllText(report,JsonSerializer.Serialize(new{side,rows,peakWorkingSetMiB=process.PeakWorkingSet64/1048576d,note="Synthetic ellipse canvas raw-active sampling, one run each Contiguous/global, original raster reference preserved. Process peak includes both runs and setup, not WPF input or natural-image complexity."},new JsonSerializerOptions{WriteIndented=true}));
    }
}
