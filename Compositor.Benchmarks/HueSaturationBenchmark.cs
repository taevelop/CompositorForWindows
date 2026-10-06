using System.Diagnostics;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;

internal static class HueSaturationBenchmark
{
    public static void Run(string path)
    {
        var settings=new HueSaturationAdjustment();
        settings=settings with {Adjustments=settings.Adjustments.SetItem(HueRange.Reds,new(Hue:45,Saturation:20))};
        var bytes=new byte[4000*4000*4];
        for(int p=0;p<bytes.Length;p+=4){bytes[p]=160;bytes[p+1]=60;bytes[p+2]=30;bytes[p+3]=180;}
        var clock=Stopwatch.StartNew();var cube=HueSaturationProcessor.Cube(settings);
        double cubeMs=clock.Elapsed.TotalMilliseconds;clock.Restart();
        NativePixels.HueCube(bytes,4000*4000,cube);
        double applyMs=clock.Elapsed.TotalMilliseconds;
        File.WriteAllText(path,JsonSerializer.Serialize(new {timeUtc=DateTimeOffset.UtcNow,description="4K RGBA alpha180 buffer, hue cube build and CPU application only; excludes renderer and WPF.",cubeMs,applyMs,workingSetMiB=Process.GetCurrentProcess().WorkingSet64/1048576d},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(File.ReadAllText(path));
    }
}
