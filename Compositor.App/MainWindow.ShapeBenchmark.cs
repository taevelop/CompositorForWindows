using System.Diagnostics;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    internal async Task ShapeBenchmark(string path)
    {
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        var original=session.Document;var tools=CaptureTabTools();
        var rows=new List<object>();var gaps=new List<double>();var paints=new List<double>();
        using var process=Process.GetCurrentProcess();var clock=Stopwatch.StartNew();double last=0;
        var timer=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(16)};
        timer.Tick+=(_,_)=>{double now=clock.Elapsed.TotalMilliseconds;gaps.Add(now-last);last=now;};
        TaskCompletionSource? frame=null;
        Canvas.PaintMeasured=(ms,changed)=>{paints.Add(ms);frame?.TrySetResult();};
        try
        {
            ToolPicker.SelectedIndex=11;BrushColor.Text="#1266AA";ShapeRadius.Value=80;ShapeWidth.Value=40;
            timer.Start();
            foreach(var kind in Enum.GetValues<ShapeKind>())
            for(int run=0;run<2;run++)
            {
                const int size=4000;
                var document=Document.Create(size,size) with{Layers=[]};session.Load(document);
                ShapeRectangle.IsChecked=kind==ShapeKind.Rectangle;ShapeEllipse.IsChecked=kind==ShapeKind.Ellipse;ShapeLine.IsChecked=kind==ShapeKind.Line;
                Canvas.RestoreView(.15,30,30);
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                long allocated=GC.GetTotalAllocatedBytes(true);var watch=Stopwatch.StartNew();
                Canvas.BeginPointer(new(0,0));Canvas.MovePointer(new(size,size));Canvas.EndPointer(true);
                if(!await Canvas.ShapeCompletion||session.UndoCount!=1)throw new InvalidOperationException("Shape did not commit once.");
                double creationMs=watch.Elapsed.TotalMilliseconds;
                frame=new(TaskCreationOptions.RunContinuationsAsynchronously);Canvas.InvalidateVisual();
                if(await Task.WhenAny(frame.Task,Task.Delay(10000))!=frame.Task)throw new TimeoutException("Shape frame did not paint.");
                double firstPaintCompleteMs=watch.Elapsed.TotalMilliseconds;frame=null;
                process.Refresh();double workingSetMiB=process.WorkingSet64/1048576d;
                double managedAllocatedMiB=(GC.GetTotalAllocatedBytes(true)-allocated)/1048576d;
                var actual=session.ActiveLayer!;
                var expected=await Task.Run(()=>ShapeRaster.Create(actual.Shape!,actual.Pixels.Width,actual.Pixels.Height));
                if(expected.Tiles.Any(p=>!actual.Pixels.Tiles[p.Key].Bytes.SequenceEqual(p.Value.Bytes)))throw new InvalidOperationException("Shape pixels differ from independent rasterization.");
                session.Undo();if(!ReferenceEquals(session.Document,document))throw new InvalidOperationException("Shape Undo lost original document.");
                rows.Add(new{kind=kind.ToString(),run,size,creationMs,firstPaintCompleteMs,workingSetMiB,managedAllocatedMiB});
            }
            var before=session.Document;
            ShapeRectangle.IsChecked=true;Canvas.BeginPointer(new(0,0));Canvas.MovePointer(new(4000,4000));Canvas.EndPointer(true);
            var pending=Canvas.ShapeCompletion;var cancel=Stopwatch.StartNew();Canvas.CancelInteraction();double immediateCancelMs=cancel.Elapsed.TotalMilliseconds;
            if(await pending||!ReferenceEquals(before,session.Document)||session.InTransaction)throw new InvalidOperationException("Cancelled shape changed the document.");
            double workerFinishedMs=cancel.Elapsed.TotalMilliseconds;
            timer.Stop();var sorted=gaps.Order().ToArray();
            if(sorted.Length==0||paints.Count==0)throw new InvalidOperationException("No WPF measurements collected.");
            System.IO.File.WriteAllText(path,JsonSerializer.Serialize(new{timeUtc=DateTimeOffset.UtcNow,
                description="Hidden actual WPF, two runs per shape, 4000x4000 canvas without background layer. Line bounds include stroke margins. Commit-to-first-Paint completion includes UI renderer work; it is not physical display latency. Working set is sampled after Paint, not peak memory. Independent pixels, single Undo and cancellation checked. 100MP, complex documents and transform redraw remain unmeasured.",
                p95DispatcherGapMs=sorted[(int)Math.Ceiling(sorted.Length*.95)-1],maxDispatcherGapMs=sorted[^1],maxPaintMs=paints.Max(),
                immediateCancelMs,workerFinishedMs,rows},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{timer.Stop();frame=null;Canvas.PaintMeasured=null;Canvas.CancelInteraction();session.Load(original);RestoreTabTools(tools);}
    }
}
