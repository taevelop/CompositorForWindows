using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;
public partial class MainWindow
{
    internal async Task HealingSmoke(string report)
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var saved = session.Document;var active = session.ActiveLayerId;var tools = CaptureTabTools();
        var originalTab = workspace.Current;
        var savedPath = projectPath;var root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!, "healing-ui-" + Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            AddDocumentTab(Document.Create(32, 32));
            var doc = Document.Create(32, 32);var bytes = Enumerable.Repeat(new byte[] { 80, 120, 160, 255 }, 1024).SelectMany(p => p).ToArray();int index = 16 * 32 + 16;
            bytes[index * 4] = bytes[index * 4 + 1] = bytes[index * 4 + 2] = 0;doc = doc.Replace(doc.Layers[0] with { Pixels = Raster.FromRgba(32, 32, bytes), Mask = LayerMask.Solid(32, 32) });
            ToolPicker.SelectedIndex = 14;BrushSize.Text = "1";BrushHardness.Text = "100";BrushOpacity.Text = "100";
            void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
            foreach (var mode in Enum.GetValues<HealingMode>())
            {
                session.Load(doc);Canvas.Fit();HealContent.IsChecked = mode == HealingMode.ContentAware;HealTexture.IsChecked = mode == HealingMode.CreateTexture;HealProximity.IsChecked = mode == HealingMode.ProximityMatch;
                Check(Canvas.Tool == EditorTool.SpotHealing && Canvas.ReadHealing() == mode && HealingOptions.IsVisible && !ColorOptions.IsVisible, "Healing controls/mode were not connected.");
                Canvas.BeginPointer(new(16.5, 16.5), ModifierKeys.None);byte wash = session.ActiveLayer!.Pixels.ToRgba()[index * 4];
                Check(wash > 0 && session.UndoCount == 0 && session.InTransaction, "Healing wash was not a draft.");
                if (mode == HealingMode.ContentAware)
                {
                    UpdateLayout();var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(this);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var output = File.Create(report + ".png");encoder.Save(output);
                }
                Canvas.EndPointer(true);Check(await Canvas.HealingCompletion, "Healing worker did not complete.");
                Check(session.UndoCount == 1 && session.ActiveLayer!.Pixels.ToRgba()[index * 4] > wash && ReferenceEquals(session.ActiveLayer.Mask, doc.Layers[0].Mask), "Healing committed wash or lost mask.");
                session.Undo();Check(ReferenceEquals(session.Document, doc), "Healing Undo lost original.");session.Redo();
            }
            session.Load(doc);Canvas.BeginPointer(new(16.5, 16.5), ModifierKeys.None);Canvas.CancelInteraction();
            Check(ReferenceEquals(session.Document, doc) && !session.InTransaction && !session.CanUndo, "Cancelled healing wash remained.");
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Canvas.HealingOverride = (original, id, coverage, opacity, mode, seed, token) => { entered.TrySetResult(true);release.Task.GetAwaiter().GetResult();return SpotHealing.Compute(original, id, coverage, opacity, mode, seed, token); };
            Canvas.BeginPointer(new(16.5, 16.5), ModifierKeys.None);Canvas.EndPointer(true);await entered.Task;
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, System.Windows.PresentationSource.FromVisual(this), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };WindowKeyDown(this, escape);release.TrySetResult(true);
            Check(escape.Handled && !await Canvas.HealingCompletion && ReferenceEquals(session.Document, doc) && !session.InTransaction, "Esc did not discard healing worker.");
            entered = new(TaskCreationOptions.RunContinuationsAsynchronously);release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Canvas.BeginPointer(new(16.5, 16.5), ModifierKeys.None);Canvas.EndPointer(true);await entered.Task;
            session.Load(doc);session.Begin();long generation = session.TransactionGeneration;release.TrySetResult(true);
            Check(!await Canvas.HealingCompletion && session.InTransaction && session.TransactionGeneration == generation, "Stale healing closed a newer transaction.");session.Cancel();
            session.Load(doc);HealContent.IsChecked = true;entered = new(TaskCreationOptions.RunContinuationsAsynchronously);release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Canvas.BeginPointer(new(16.5, 16.5), ModifierKeys.None);Canvas.EndPointer(true);await entered.Task;
            projectPath = Path.Combine(root, "Healing.comp");var saving = Save(false);
            Check(!saving.IsCompleted && busy && !Editor.IsEnabled && !Directory.Exists(projectPath), "Save did not await healing privately.");
            release.TrySetResult(true);Check(await saving && !session.IsModified && session.UndoCount == 1, "Pending healing save failed.");
            var savedResult = session.Document;Check(CompositePixels(savedResult).SequenceEqual(CompositePixels(ProjectStore.Load(projectPath).Document)), "Saved healing pixels differ.");
            Check(await ReopenSavedProject(projectPath) && CompositePixels(savedResult).SequenceEqual(CompositePixels(session.Document)), "Healing UI reopen differs.");
            var png = Path.Combine(root, "Healing.png");ImageCodec.Export(session.Document, png, false);Check(CompositePixels(session.Document).SequenceEqual(ImageCodec.Load(png).ToRgba()), "Healing PNG differs from canvas.");
            session.Load(doc);ToolPicker.SelectedIndex = 14;BrushSize.Text = "1";BrushHardness.Text = "100";BrushOpacity.Text = "100";HealContent.IsChecked = true;
            entered = new(TaskCreationOptions.RunContinuationsAsynchronously);release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Canvas.BeginPointer(new(16.5, 16.5), ModifierKeys.None);Canvas.EndPointer(true);await entered.Task;
            projectPath = Path.Combine(root, "Cancelled.comp");var cancelledSave = Save(false);
            var cancelKey = new KeyEventArgs(Keyboard.PrimaryDevice, System.Windows.PresentationSource.FromVisual(this), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };WindowKeyDown(this, cancelKey);release.TrySetResult(true);
            Check(cancelKey.Handled && !await cancelledSave && ReferenceEquals(session.Document, doc) && !Directory.Exists(projectPath) && !busy && Editor.IsEnabled, "Cancelled save wrote a healing draft or left UI locked.");
            HealTexture.IsChecked = true;var state = CaptureTabTools();HealContent.IsChecked = true;RestoreTabTools(state);Check(Canvas.ReadHealing() == HealingMode.CreateTexture, "Healing mode snapshot did not restore.");
            File.WriteAllText(report, JsonSerializer.Serialize(new { checks = new[] { "three modes", "dark wash draft", "source healing instead of wash", "single Undo/Redo", "mask preservation", "gesture cancel", "Esc and late worker", "newer transaction preserved", "mode snapshot restore", "pending save waits for heal", "UI save/reopen and PNG match", "Esc during save creates no project" }, note = "Injected WPF control/pointer path. Raster expansion, large/physical input and full parity remain separate." }));
        }
        finally
        {
            Canvas.HealingOverride = null;Canvas.CancelInteraction();
            if (workspace.Current.Id != originalTab.Id)
            {
                var fixture = workspace.Current.Id;session.Load(Document.Create(1, 1));workspace.Close(fixture, true);tabViews.Remove(fixture);BindCurrentTab();
            }
            SelectTab(originalTab.Id);session.Load(saved, active);RestoreTabTools(tools);projectPath = savedPath;Refresh();
            string target = Path.GetFullPath(root), parent = Path.GetFullPath(Path.GetDirectoryName(Path.GetFullPath(report))!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("healing-ui-")) throw new IOException("Unsafe healing UI cleanup.");
            Directory.Delete(target, true);
        }
    }
    internal async Task HealingToolSmoke(string report)
    { await HealingSmoke(report);foreach (var tab in workspace.Documents) tab.Session.MarkSaved();Close(); }
}
