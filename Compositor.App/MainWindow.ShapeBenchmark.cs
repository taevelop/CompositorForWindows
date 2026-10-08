using System.Diagnostics;
using System.Collections.Immutable;
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
                Canvas.EnsurePrepared(session.Document);await Canvas.RenderPreparation;
                if(!Canvas.EnsurePrepared(session.Document))throw new InvalidOperationException("Shape viewport preparation was not installed.");
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
            var transformDocument=await Task.Run(()=>
            {
                var layers=Enumerable.Range(0,4).Select(i=>
                {
                    var style=new LayerShapeStyle(ShapeKind.Rectangle,.2+i*.15,.4,.7,CornerRadius:80);
                    return new Layer(Guid.NewGuid(),"Shape "+i,ShapeRaster.Create(style,1024,1024),new((i%2)*1400,(i/2)*1400,1024,1024)){Shape=style};
                }).ToImmutableArray();
                return Document.Create(4000,4000) with{Layers=layers};
            });
            session.Load(transformDocument);session.SelectLayers(transformDocument.Layers.Select(l=>l.Id));Canvas.RestoreView(.15,30,30);Canvas.BeginTransform();
            var initial=Canvas.TransformInitial!;var previews=new List<double>();
            for(int step=0;step<4;step++)
            {
                var watch=Stopwatch.StartNew();Canvas.PreviewTransform(initial with{Width=initial.Width*(1.2+step*.1),Height=initial.Height*(1.2+step*.1)});
                var display=Canvas.DisplayDocument;previews.Add(watch.Elapsed.TotalMilliseconds);
                if(display.Layers.Any(l=>l.Pixels.Width>2048||l.Pixels.Height>2048)||session.Document.Layers.Any(l=>!ReferenceEquals(l.Pixels,transformDocument.Layers.First(o=>o.Id==l.Id).Pixels)))
                    throw new InvalidOperationException("Transform preview exceeded its grid or replaced document pixels.");
                frame=new(TaskCreationOptions.RunContinuationsAsynchronously);Canvas.InvalidateVisual();
                if(await Task.WhenAny(frame.Task,Task.Delay(10000))!=frame.Task)throw new TimeoutException("Transform frame did not paint.");frame=null;
            }
            var scaledDisplay=Canvas.DisplayDocument;var moveWatch=Stopwatch.StartNew();Canvas.PreviewTransform(Canvas.TransformDraft! with{X=20,Y=20,Rotation=15});
            var movedDisplay=Canvas.DisplayDocument;double movePreviewMs=moveWatch.Elapsed.TotalMilliseconds;
            if(scaledDisplay.Layers.Where((l,i)=>!ReferenceEquals(l.Pixels,movedDisplay.Layers[i].Pixels)).Any())throw new InvalidOperationException("Move/rotation reallocated shape previews.");
            var commitWatch=Stopwatch.StartNew();Canvas.CommitTransform();double transformCommitMs=commitWatch.Elapsed.TotalMilliseconds;
            if(session.UndoCount!=1)throw new InvalidOperationException("Group shape transform did not commit once.");
            var final=session.Document;
            await Task.Run(()=>
            {
                foreach(var layer in final.Layers)
                {
                    var expected=ShapeRaster.Create(layer.Shape!,layer.Pixels.Width,layer.Pixels.Height);
                    if(expected.Tiles.Any(p=>!layer.Pixels.Tiles[p.Key].Bytes.SequenceEqual(p.Value.Bytes)))throw new InvalidOperationException("Transformed shape pixels differ.");
                }
            });
            session.Undo();if(!ReferenceEquals(session.Document,transformDocument))throw new InvalidOperationException("Group transform Undo lost original.");
            Canvas.BeginTransform();Canvas.PreviewTransform(initial with{Width=initial.Width*1.5,Height=initial.Height*1.5});_ = Canvas.DisplayDocument;Canvas.CancelTransform();
            if(!ReferenceEquals(session.Document,transformDocument)||!ReferenceEquals(Canvas.DisplayDocument,transformDocument))throw new InvalidOperationException("Group transform cancellation retained preview.");
            timer.Stop();var sorted=gaps.Order().ToArray();
            if(sorted.Length==0||paints.Count==0)throw new InvalidOperationException("No WPF measurements collected.");
            System.IO.File.WriteAllText(path,JsonSerializer.Serialize(new{timeUtc=DateTimeOffset.UtcNow,
                description="Hidden actual WPF, two runs per shape, 4000x4000 canvas without background layer, then four 1024px rounded rectangles transformed together through four scales and move/rotation. Line bounds include stroke margins. Pointer-start-to-first-ready-Paint includes preparation and UI rendering, not physical display latency. Working set is sampled after creation Paint, not peak memory. Independent pixels, single Undo and cancellation checked. 100MP, effects/adjustments and physical input remain unmeasured.",
                p95DispatcherGapMs=sorted[(int)Math.Ceiling(sorted.Length*.95)-1],maxDispatcherGapMs=sorted[^1],maxPaintMs=paints.Max(),
                immediateCancelMs,workerFinishedMs,transformPreviewMs=previews,movePreviewMs,transformCommitMs,rows},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{timer.Stop();frame=null;Canvas.PaintMeasured=null;Canvas.CancelInteraction();session.Load(original);RestoreTabTools(tools);}
    }
}
