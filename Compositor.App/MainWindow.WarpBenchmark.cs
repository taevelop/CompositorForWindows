using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;
public partial class MainWindow
{
    internal async Task WarpBenchmark(string report, int side)
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var doc = Document.Create(side, side);var bytes = new byte[checked(side * side * 4)];
        for (int y = 0; y < side; y++) for (int x = 0; x < side; x++) { int p = (y * side + x) * 4;bytes[p + (x < side / 2 ? 0 : 1)] = 100;bytes[p + 3] = 128; }
        doc = doc.Replace(doc.Layers[0] with { Pixels = Raster.FromRgba(side, side, bytes) });var target = new PointD(side / 2d - 4, side / 2d);var rows = new List<object>();
        foreach (var mode in new[] { SmearMode.Smudge, SmearMode.Liquify })
        {
            session.Load(doc);ToolPicker.SelectedIndex = 15;SmearSmudge.IsChecked = mode == SmearMode.Smudge;SmearLiquify.IsChecked = mode == SmearMode.Liquify;
            BrushSize.Text = "800";BrushHardness.Text = "70";BrushOpacity.Text = "60";Canvas.Fit();
            var initialClock = Stopwatch.StartNew();Canvas.InvalidateVisual();await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);double initialDisplayMs = initialClock.Elapsed.TotalMilliseconds;
            long previous = Stopwatch.GetTimestamp();double maximumGap = 0, maximumInput = 0, maximumPaint = 0, paintTotal = 0;int ticks = 0, paints = 0;
            var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { long now = Stopwatch.GetTimestamp();maximumGap = Math.Max(maximumGap, Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds);previous = now;ticks++; };
            Canvas.PaintMeasured = (elapsed, _) => { paints++;paintTotal += elapsed;maximumPaint = Math.Max(maximumPaint, elapsed); };
            long allocated = GC.GetTotalAllocatedBytes(true);var clock = Stopwatch.StartNew();timer.Start();double queuedInputMs, completionMs;
            try
            {
                Canvas.BeginPointer(target, ModifierKeys.None);maximumInput = clock.Elapsed.TotalMilliseconds;
                if (!Canvas.IsWarpPreparing || !ReferenceEquals(session.Document, doc)) throw new InvalidOperationException("Warp prepared outside private transaction.");
                for (int i = 1; i < 48; i++)
                { long start = Stopwatch.GetTimestamp();Canvas.MovePointer(new(target.X + i * 8, target.Y));maximumInput = Math.Max(maximumInput, Stopwatch.GetElapsedTime(start).TotalMilliseconds);await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background); }
                queuedInputMs = clock.Elapsed.TotalMilliseconds;Canvas.EndPointer(true);
                if (!await Canvas.WarpCompletion) throw new InvalidOperationException("Large Warp failed.");completionMs = clock.Elapsed.TotalMilliseconds;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            }
            finally { timer.Stop();Canvas.PaintMeasured = null; }
            if (session.UndoCount != 1 || session.InTransaction || ticks == 0 || ReferenceEquals(session.Document, doc)) throw new InvalidOperationException("Warp lost result, Undo, or Dispatcher progress.");
            var result = session.Document;result.Validate();session.Undo();if (!ReferenceEquals(session.Document, doc)) throw new InvalidOperationException("Warp Undo lost source.");
            Canvas.BeginPointer(target, ModifierKeys.None);await Task.Delay(10);
            if (!Canvas.IsWarpPreparing) throw new InvalidOperationException("Warp finished before cancellation probe.");
            clock.Restart();var escape = new KeyEventArgs(Keyboard.PrimaryDevice, System.Windows.PresentationSource.FromVisual(this), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            WindowKeyDown(this, escape);double cancelUiMs = clock.Elapsed.TotalMilliseconds;
            if (!escape.Handled || session.InTransaction || !ReferenceEquals(session.Document, doc) || await Canvas.WarpCompletion) throw new InvalidOperationException("Large Warp Esc published an edit.");
            double cancelledWorkerMs = clock.Elapsed.TotalMilliseconds;using var process = Process.GetCurrentProcess();process.Refresh();
            rows.Add(new { mode = mode.ToString(), initialDisplayMs, queuedInputMs, completionMs, maximumInputMs = maximumInput, ticks, maximumDispatcherGapMs = maximumGap, paints, paintTotalMs = paintTotal, maximumPaintMs = maximumPaint, cancelUiMs, cancelledWorkerMs,
                allocatedMiB = (GC.GetTotalAllocatedBytes(true) - allocated) / 1048576d, peakMiB = process.PeakWorkingSet64 / 1048576d });
        }
        File.WriteAllText(report, JsonSerializer.Serialize(new { side, brush = 800, points = 48, rows,
            note = "One actual WPF dense synthetic split per mode after initial display; queued input with Background yields. Completion includes capture, dabs, final copy and UI commit; paints exclude WPF bitmap presentation. Esc after10ms while owner active; cancellation phase unproven. Allocation includes cancellation/validation; peak is process lifetime across both modes including setup. Not physical input, natural quality,100MP, or Mac parity." }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var tab in workspace.Documents) tab.Session.MarkSaved();Close();
    }
}
