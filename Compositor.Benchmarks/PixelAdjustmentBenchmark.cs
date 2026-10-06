using System.Diagnostics;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;
internal static class PixelAdjustmentBenchmark
{
    public static void Run(string path)
    {
        const int w=4000,h=4000;var bytes=new byte[w*h*4];
        for(int p=0;p<bytes.Length;p+=4){bytes[p]=160;bytes[p+1]=80;bytes[p+2]=40;bytes[p+3]=180;}
        var source=Raster.FromRgba(w,h,bytes);var rows=new List<object>();
        foreach(var adjustment in new[]{
            Layer.ExposureLayer(w,h) with{Exposure=new(1)},
            Layer.HueSaturationLayer(w,h) with{HueSaturation=new(60,20)},
            Layer.ColorBalanceLayer(w,h) with{ColorBalance=new(MidMagentaGreen:30)},
            Layer.GrainLayer(w,h) with{Grain=new(50,2,70,123)}})
        {
            long before=GC.GetTotalAllocatedBytes(true);var clock=Stopwatch.StartNew();var result=PixelAdjustments.Apply(source,adjustment);
            rows.Add(new{adjustment.Name,milliseconds=clock.Elapsed.TotalMilliseconds,allocatedMiB=(GC.GetTotalAllocatedBytes(true)-before)/1048576d,
                changedTiles=result.Tiles.Count(p=>!ReferenceEquals(p.Value,source.Tiles.GetValueOrDefault(p.Key))),workingSetMiB=Process.GetCurrentProcess().WorkingSet64/1048576d});
        }
        File.WriteAllText(path,JsonSerializer.Serialize(new{timeUtc=DateTimeOffset.UtcNow,description="4K source pixel adjustment including RGBA packing and immutable tile reconstruction; excludes WPF and final scene rendering.",rows},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(File.ReadAllText(path));
    }
}
