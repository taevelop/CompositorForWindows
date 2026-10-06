using System.Diagnostics;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;
internal static class ColorBalanceGrainBenchmark
{
    public static void Run(string path)
    {
        var d=Document.Create(4000,4000);var bytes=new byte[4000*4000*4];
        for(int p=0;p<bytes.Length;p+=4){bytes[p]=80;bytes[p+1]=120;bytes[p+2]=40;bytes[p+3]=160;}
        d=d.Replace(d.Layers[0] with {Pixels=Raster.FromRgba(4000,4000,bytes)});var rows=new List<object>();
        foreach(bool grain in new[]{false,true})
        {
            using var r=new ViewportRenderer();var adjustment=grain?Layer.GrainLayer(4000,4000) with {Grain=new(Seed:123)}:Layer.ColorBalanceLayer(4000,4000) with {ColorBalance=new(MidCyanRed:20)};
            var current=d with {Layers=d.Layers.Add(adjustment)};var clock=Stopwatch.StartNew();
            using(var image=r.Render(current,1000,1000,.25f,0,0)){}
            double initialMs=clock.Elapsed.TotalMilliseconds;clock.Restart();
            using(var image=r.Render(current,1000,1000,.25f,2,3)){}
            rows.Add(new{grain,initialMs,cachedPanMs=clock.Elapsed.TotalMilliseconds,workingSetMiB=Process.GetCurrentProcess().WorkingSet64/1048576d});
        }
        File.WriteAllText(path,JsonSerializer.Serialize(new{timeUtc=DateTimeOffset.UtcNow,description="4K half-transparent source and one adjustment, 1000px CPU viewport. Excludes WPF.",rows},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(File.ReadAllText(path));
    }
}
