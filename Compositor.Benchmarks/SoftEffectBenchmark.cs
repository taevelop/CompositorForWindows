using System.Diagnostics;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;

internal static class SoftEffectBenchmark
{
    public static void Run(string path)
    {
        var d = Document.Create(4000,4000); var data = new byte[4000*4000*4];
        for (int y=1000;y<3000;y++) for(int x=1000;x<3000;x++) {int p=(y*4000+x)*4;data[p]=100;data[p+1]=150;data[p+2]=200;data[p+3]=255;}
        var source=d.Layers[0] with { Pixels=Raster.FromRgba(4000,4000,data) };
        var rows=new List<object>();
        foreach(bool inner in new[]{true,false})
        {
            using var r=new ViewportRenderer(); var current=d.Replace(source with {Effects=inner ? new(InnerShadow:new(120,20,20)) : new(OuterGlow:new(20))});
            var clock=Stopwatch.StartNew(); using(var image=r.Render(current,1000,1000,.25f,0,0)) {}
            double initialMs=clock.Elapsed.TotalMilliseconds;
            clock.Restart();using(var image=r.Render(current,1000,1000,.25f,2,3)) {}
            rows.Add(new{inner,initialMs,cachedPanMs=clock.Elapsed.TotalMilliseconds,workingSetMiB=Process.GetCurrentProcess().WorkingSet64/1048576d});
        }
        File.WriteAllText(path,JsonSerializer.Serialize(new{timeUtc=DateTimeOffset.UtcNow,description="4K layer, 1000px CPU viewport; inner shadow and outer glow, 20px blur; cold render and cached pan. Excludes WPF.",rows},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(File.ReadAllText(path));
    }
}
