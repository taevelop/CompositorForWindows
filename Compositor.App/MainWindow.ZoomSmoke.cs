using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;
public partial class MainWindow
{
    internal async Task ZoomToolSmoke(string report)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var doc = Document.Create(800, 600);session.Load(doc);ToolPicker.SelectedIndex = 16;Canvas.ActualPixels();
        Check(Canvas.Tool == EditorTool.Zoom && ZoomOptions.IsVisible && !BrushOptions.IsVisible && ToolTitle.Text == "Zoom", "Zoom controls missing.");
        var point = new Point(200, 150);var anchor = Canvas.DocumentPoint(point);
        Check(Canvas.BeginInteraction(MouseButton.Left, point, modifiers: ModifierKeys.None), "Zoom click did not begin.");
        Check(Canvas.FinishInteraction(MouseButton.Left, point, ModifierKeys.None) && Canvas.Zoom == 2, "Zoom click did not double.");
        Check(double.Hypot(Canvas.DocumentPoint(point).X - anchor.X, Canvas.DocumentPoint(point).Y - anchor.Y) < 1e-8, "Zoom moved anchor.");
        Canvas.BeginInteraction(MouseButton.Left, point, modifiers: ModifierKeys.Alt);Canvas.FinishInteraction(MouseButton.Left, point, ModifierKeys.Alt);Check(Canvas.Zoom == 1, "Alt zoom did not halve.");
        var initial = Canvas.CaptureView();Canvas.BeginInteraction(MouseButton.Left, point, modifiers: ModifierKeys.None);Canvas.MoveInteraction(new(point.X + 100, point.Y));Check(Canvas.Zoom == 2, "Scrub zoom did not double after100px.");
        Canvas.CancelInteraction();Check(Canvas.CaptureView() == initial && !Canvas.HasInteraction, "Esc/cancel did not restore zoom/pan.");
        Canvas.BeginInteraction(MouseButton.Left, point, modifiers: ModifierKeys.None);Canvas.MoveInteraction(new(point.X - 100, point.Y));Canvas.FinishInteraction(MouseButton.Left, new(point.X - 100, point.Y), ModifierKeys.None);Check(Canvas.Zoom == .5, "Scrub zoom out clicked again on release.");
        ZoomSlider.Value = 250;Check(Canvas.Zoom == 2.5 && ZoomPercent.Text == "250", "Slider/percent did not sync.");
        ZoomPercent.Text = "0.1%";CommitZoomInput();Check(Canvas.Zoom == .001, "Minimum percentage failed.");ZoomPercent.Text = "9999";CommitZoomInput();Check(Canvas.Zoom == 32, "Percentage clamp failed.");
        ZoomPercent.Text = "invalid";CommitZoomInput();Check(Canvas.Zoom == 32 && ZoomPercent.Text == "3200", "Invalid draft changed zoom.");
        ZoomPercent.Text = "100";StepZoom(true, false);Check(Canvas.Zoom == 1.01, "One percent step failed.");StepZoom(false, true);Check(Canvas.Zoom == .91, "Shift ten percent step failed.");
        Canvas.ActualPixels();var middle = Canvas.CaptureView();Canvas.BeginInteraction(MouseButton.Middle, point, modifiers: ModifierKeys.None);Canvas.MoveInteraction(new(point.X + 10, point.Y + 20));Canvas.CancelInteraction();Check(Canvas.CaptureView() == middle, "Middle pan cancel failed with Zoom.");
        ToolPicker.SelectedIndex = 15;SmearLiquify.IsChecked = true;CycleSmearMode();Check(Canvas.ReadSmear() == SmearMode.Blur, "First mode cycle failed.");CycleSmearMode();Check(Canvas.ReadSmear() == SmearMode.Smudge, "Second mode cycle failed.");CycleSmearMode();Check(Canvas.ReadSmear() == SmearMode.Liquify, "Mode cycle did not wrap.");
        session.Begin();CycleSmearMode();Check(Canvas.ReadSmear() == SmearMode.Liquify, "Mode cycled during transaction.");session.Cancel();
        Check(ReferenceEquals(doc, session.Document) && session.UndoCount == 0 && !session.IsModified, "Navigation modified document/history.");
        File.WriteAllText(report, JsonSerializer.Serialize(new { passed = true, checks = new[] { "click/Alt anchor", "horizontal scrub", "cancel viewport", "percent slider/draft/clamp", "middle pan", "mode cycle/guard", "document/history unchanged" } }));
        foreach (var tab in workspace.Documents) tab.Session.MarkSaved();Close();
    }
}
