using System.Diagnostics;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;
internal static class AddNoiseBenchmark
{
    public static void Run(string path)
    {
        var source=ShapeRaster.Create(new(ShapeKind.Rectangle,.5,.5,.5),4000,4000);var rows=new List<object>();
        foreach(var distribution in Enum.GetValues<NoiseDistribution>())
        {
            long allocated=GC.GetTotalAllocatedBytes(true);var clock=Stopwatch.StartNew();var result=AddNoise.Apply(source,new(10,distribution,true,7));double elapsedMs=clock.Elapsed.TotalMilliseconds;
            if(result.Width!=source.Width||result.Height!=source.Height||result.Tiles.Any(p=>p.Value.Bytes.WhereAlphaChanged(source.Tiles[p.Key])))throw new InvalidOperationException("Noise changed dimensions or alpha.");
            rows.Add(new{distribution=distribution.ToString(),elapsedMs,allocatedMiB=(GC.GetTotalAllocatedBytes(true)-allocated)/1048576d});
        }
        File.WriteAllText(path,JsonSerializer.Serialize(new{timeUtc=DateTimeOffset.UtcNow,description="4000x4000 opaque gray, amount 10%, monochromatic, seed 7, one run per distribution. Core elapsed and cumulative managed allocation; excludes WPF latency and peak memory.",rows},new JsonSerializerOptions{WriteIndented=true}));
    }
    private static bool WhereAlphaChanged(this ReadOnlySpan<byte> after,PixelTile before)
    {
        for(int i=3;i<after.Length;i+=4)if(after[i]!=before.Bytes[i])return true;return false;
    }
}
