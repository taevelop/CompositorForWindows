using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Compositor.Core;
using Compositor.Imaging;
using Microsoft.Win32;

namespace Compositor.App;

public partial class MainWindow : Window
{
    private readonly EditorSession session = new(Document.Create(1400, 900));
    private string? projectPath;
    private readonly EditorPrompts defaultPrompts;
    private EditorPrompts prompts;
    private bool refreshing, busy, allowClose;

    public MainWindow()
    {
        defaultPrompts = prompts = EditorPrompts.For(this);
        InitializeComponent();
        Canvas.Session = session; Canvas.ReadBrush = ReadBrush;
        Canvas.ReportError = ShowError;
        Canvas.ViewportChanged = UpdateStatus;
        Deactivated += (_, _) => Canvas.CancelInteraction();
        LayerBlend.ItemsSource = Enum.GetValues<BlendMode>();
        LayerSampling.ItemsSource = Enum.GetValues<Sampling>();
        session.Changed += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        Canvas.InvalidateVisual();
        Title = $"{(projectPath is null ? "Untitled" : Path.GetFileName(projectPath))}{(session.IsModified ? " *" : "")} — Compositor for Windows";
        if (session.InTransaction) return;
        refreshing = true;
        Layers.ItemsSource = session.Document.Layers.Reverse().ToArray();
        Layers.SelectedItem = session.ActiveLayer;
        if (session.ActiveLayer is { } layer)
        {
            LayerName.Text = layer.Name; LayerVisible.IsChecked = layer.Visible;
            LayerOpacity.Text = F(layer.Opacity * 100); LayerBlend.SelectedItem = layer.Blend;
            LayerX.Text = F(layer.Transform.X); LayerY.Text = F(layer.Transform.Y);
            LayerWidth.Text = F(layer.Transform.Width); LayerHeight.Text = F(layer.Transform.Height);
            LayerRotation.Text = F(layer.Transform.Rotation);
            FlipX.IsChecked = layer.Transform.FlipX; FlipY.IsChecked = layer.Transform.FlipY;
            LayerSampling.SelectedItem = layer.Transform.Sampling;
        }
        UndoMenu.IsEnabled = session.CanUndo; RedoMenu.IsEnabled = session.CanRedo;
        UpdateStatus();
        refreshing = false;
    }
    private void UpdateStatus()
    {
        if (!busy) Status.Text = $"{session.Document.Width:N0} × {session.Document.Height:N0} px   ·   {session.Document.Layers.Length} layers   ·   {Canvas.Zoom:P0}   ·   {Canvas.Tool}";
    }
    private static string F(double n) => n.ToString("0.###", CultureInfo.InvariantCulture);
    private static double Number(TextBox input)
    {
        if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || !double.IsFinite(n))
            throw new InvalidDataException($"Invalid number: {input.Text}");
        return n;
    }
    private BrushSettings ReadBrush()
    {
        var color = (Color)ColorConverter.ConvertFromString(BrushColor.Text);
        if (color.A != 255) throw new InvalidDataException("Use an opaque RGB color; control transparency with brush opacity.");
        return new(Number(BrushSize), Number(BrushHardness) / 100, Number(BrushOpacity) / 100, color.R, color.G, color.B);
    }
    private void ShowError(string message) => prompts.ShowError(message);
    private void Safe(Action action)
    {
        if (busy || session.InTransaction) return;
        try { action(); } catch (Exception e) { ShowError(e.Message); }
    }
    private async Task<bool> Work(string label, Action action)
    {
        if (busy || session.InTransaction) return false;
        busy = true; Editor.IsEnabled = false; Status.Text = label;
        try { await Task.Run(action); return true; }
        catch (Exception e) { ShowError(e.Message); return false; }
        finally { busy = false; Editor.IsEnabled = true; Refresh(); }
    }
    private async Task<bool> ConfirmDiscard()
    {
        if (busy || session.InTransaction) return false;
        if (!session.IsModified) return true;
        var response = prompts.ConfirmSaveChanges();
        return response == MessageBoxResult.No || (response == MessageBoxResult.Yes && await Save(false));
    }
    private async void NewDocument(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscard()) return;
        string? value = Prompt("New canvas", "Width × height in pixels", "1400 x 900");
        if (value is null) return;
        Safe(() =>
        {
            var dimensions = value.ToLowerInvariant().Replace('×', 'x').Split('x');
            if (dimensions.Length != 2 || !int.TryParse(dimensions[0].Trim(), out int w) || !int.TryParse(dimensions[1].Trim(), out int h))
                throw new InvalidDataException("Enter dimensions such as 1400 x 900.");
            var doc = Document.Create(w, h); session.Load(doc); projectPath = null;
            // An untitled blank document is clean until the first edit.
            Canvas.Fit(); Refresh();
        });
    }
    private async void OpenProject(object? sender, RoutedEventArgs e) => await OpenProjectFolder(false);
    private async void OpenRecovery(object? sender, RoutedEventArgs e) => await OpenProjectFolder(true);
    private async Task OpenProjectFolder(bool recoveryOnly)
    {
        if (!await ConfirmDiscard()) return;
        var dialog = new OpenFolderDialog { Title = recoveryOnly ? "Select a .comp.recovery folder" : "Select a .comp project folder" };
        if (dialog.ShowDialog(this) != true) return;
        bool recovery = recoveryOnly || dialog.FolderName.EndsWith(".comp.recovery", StringComparison.OrdinalIgnoreCase);
        await LoadProject(dialog.FolderName, recovery);
    }
    private async Task<bool> LoadProject(string source, bool recovery)
    {
        LoadedProject? loaded = null;
        if (!await Work("Opening project…", () => loaded = recovery ? ProjectStore.LoadRecovery(source) : ProjectStore.Load(source))) return false;
        OpenLoadedProject(loaded!, source, recovery); return true;
    }
    private void OpenLoadedProject(LoadedProject loaded, string source, bool recovery)
    {
        session.Load(loaded.Document, loaded.ActiveLayerId, recovered: recovery);
        projectPath = recovery ? null : source; Canvas.Fit(); Refresh();
        if (recovery) Status.Text = "Recovery copy opened. Save to a new project name; the original and recovery folders are preserved.";
    }
    private async void ImportImages(object? sender, RoutedEventArgs e)
    {
        if (busy || session.InTransaction) return;
        var dialog = new OpenFileDialog { Title = "Import images as layers", Filter = "Images|*.png;*.jpg;*.jpeg", Multiselect = true };
        if (dialog.ShowDialog(this) == true) await Import(dialog.FileNames);
    }
    private async Task Import(string[] paths)
    {
        var before = session.Document; var imported = new List<Layer>();
        bool success = await Work("Importing images…", () =>
        {
            long used = before.Layers.Where(l => l.Pixels.Tiles.Count != 0).Sum(l => (long)l.Pixels.Width * l.Pixels.Height);
            foreach (string path in paths)
            {
                var pixels = ImageCodec.Load(path, Limits.MaxPixels - used); used += (long)pixels.Width * pixels.Height;
                imported.Add(new(Guid.NewGuid(), Path.GetFileNameWithoutExtension(path), pixels,
                    new((before.Width - pixels.Width) / 2.0, (before.Height - pixels.Height) / 2.0, pixels.Width, pixels.Height)));
            }
            (before with { Layers = before.Layers.AddRange(imported) }).Validate();
        });
        if (success && imported.Count > 0)
        { session.Apply(d => d with { Layers = d.Layers.AddRange(imported) }); session.ActiveLayerId = imported[^1].Id; Refresh(); }
    }
    private async void DropFiles(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && !busy && !session.InTransaction) await Import(files);
    }
    private async Task<bool> Save(bool saveAs)
    {
        if (busy || session.InTransaction) return false;
        string? destination = projectPath;
        if (saveAs || destination is null)
        {
            var folder = new OpenFolderDialog { Title = "Choose the folder in which to save your project" };
            if (folder.ShowDialog(this) != true) return false;
            string? name = Prompt("Save project", "Project folder name", projectPath is null ? "Untitled.comp" : Path.GetFileName(projectPath));
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or ".." || name.EndsWith(' ') || name.EndsWith('.'))
            { ShowError("Enter a valid project name without path separators."); return false; }
            if (!name.EndsWith(".comp", StringComparison.OrdinalIgnoreCase)) name += ".comp";
            destination = Path.Combine(folder.FolderName, name);
            if (Directory.Exists(destination) && MessageBox.Show(this, "Replace this existing project?", "Save project", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return false;
        }
        var document = session.Document; var active = session.ActiveLayerId;
        if (!await Work("Saving project…", () => ProjectStore.Save(document, active, destination))) return false;
        projectPath = destination; session.MarkSaved(); Refresh(); return true;
    }
    private async void SaveProject(object? sender, RoutedEventArgs e) => await Save(false);
    private async void SaveProjectAs(object? sender, RoutedEventArgs e) => await Save(true);
    private async Task Export(bool jpeg)
    {
        if (busy || session.InTransaction) return;
        var dialog = new SaveFileDialog { Title = "Export flattened image", Filter = jpeg ? "JPEG image|*.jpg" : "PNG image|*.png", DefaultExt = jpeg ? ".jpg" : ".png", FileName = "Untitled" };
        if (dialog.ShowDialog(this) != true) return;
        var document = session.Document;
        await Work("Exporting image…", () => ImageCodec.Export(document, dialog.FileName, jpeg));
    }
    private async void ExportPng(object sender, RoutedEventArgs e) => await Export(false);
    private async void ExportJpeg(object sender, RoutedEventArgs e) => await Export(true);
    private void AddLayer(object sender, RoutedEventArgs e) => Safe(() =>
    {
        var l = Layer.Blank($"Layer {session.Document.Layers.Length + 1}", session.Document.Width, session.Document.Height);
        session.Apply(d => d with { Layers = d.Layers.Add(l) }); session.ActiveLayerId = l.Id; Refresh();
    });
    private void DeleteLayer(object? sender, RoutedEventArgs e) => Safe(() =>
    {
        if (session.ActiveLayer is not { } l) return;
        var remaining = session.Document.Layers.Remove(l);
        session.Apply(d => d with { Layers = remaining });
    });
    private void Reorder(int offset) => Safe(() =>
    {
        if (session.ActiveLayer is not { } l) return;
        int index = session.Document.Layers.IndexOf(l), target = index + offset;
        if (target < 0 || target >= session.Document.Layers.Length) return;
        session.Apply(d => d with { Layers = d.Layers.RemoveAt(index).Insert(target, l) });
    });
    private void LayerUp(object sender, RoutedEventArgs e) => Reorder(1);
    private void LayerDown(object sender, RoutedEventArgs e) => Reorder(-1);
    private void LayerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || busy || session.InTransaction || Layers.SelectedItem is not Layer layer) return;
        session.ActiveLayerId = layer.Id; Refresh();
    }
    private void ApplyLayer(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (session.ActiveLayer is not { } l) return;
        var t = new LayerTransform(Number(LayerX), Number(LayerY), Number(LayerWidth), Number(LayerHeight), Number(LayerRotation),
            FlipX.IsChecked == true, FlipY.IsChecked == true, (Sampling)LayerSampling.SelectedItem);
        var updated = l with { Name = LayerName.Text, Visible = LayerVisible.IsChecked == true,
            Opacity = Number(LayerOpacity) / 100, Blend = (BlendMode)LayerBlend.SelectedItem, Transform = t };
        session.Apply(d => d.Replace(updated)); Canvas.Focus();
    });
    private void ToolChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Canvas is null) return;
        Canvas.CancelInteraction();
        Canvas.Tool = (EditorTool)ToolPicker.SelectedIndex; Canvas.InvalidateVisual(); Canvas.Focus();
    }
    private void Undo(object? sender, RoutedEventArgs e) => Safe(session.Undo);
    private void Redo(object? sender, RoutedEventArgs e) => Safe(session.Redo);
    private void Fit(object? sender, RoutedEventArgs e) { if (Canvas?.Session is not null) { Canvas.Fit(); Refresh(); } }
    private void ActualPixels(object? sender, RoutedEventArgs e) { Canvas.ActualPixels(); Refresh(); }
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (busy) return;
        if (e.Key == Key.Escape && Canvas.HasInteraction) { Canvas.CancelInteraction(); e.Handled = true; return; }
        // Text editing owns its own shortcuts, including Undo and Delete.
        if (Keyboard.FocusedElement is TextBox) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (session.InTransaction) return;
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.N: NewDocument(null, e); break; case Key.O: OpenProject(null, e); break; case Key.I: ImportImages(null, e); break;
                case Key.S: if (shift) SaveProjectAs(null, e); else SaveProject(null, e); break;
                case Key.Z: if (shift) Redo(null, e); else Undo(null, e); break; case Key.Y: Redo(null, e); break;
                case Key.D0: Fit(null, e); break; case Key.D1: ActualPixels(null, e); break; default: return;
            }
        }
        else
        {
            switch (e.Key)
            {
                case Key.V: ToolPicker.SelectedIndex = 0; break; case Key.B: ToolPicker.SelectedIndex = 1; break;
                case Key.E: ToolPicker.SelectedIndex = 2; break; case Key.H: ToolPicker.SelectedIndex = 3; break;
                case Key.Delete: DeleteLayer(null, e); break; default: return;
            }
        }
        e.Handled = true;
    }
    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        Canvas.CancelInteraction();
        e.Cancel = true;
        if (await ConfirmDiscard()) { allowClose = true; _ = Dispatcher.BeginInvoke(new Action(Close)); }
    }
    private string? Prompt(string title, string label, string initial)
    {
        var field = new TextBox { Text = initial, MinWidth = 340 };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel); buttons.Children.Add(ok);
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(3, 0, 3, 12) }); panel.Children.Add(field); panel.Children.Add(buttons);
        var dialog = new Window { Title = title, Owner = this, Content = panel, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        ok.Click += (_, _) => dialog.DialogResult = true;
        dialog.Loaded += (_, _) => { field.Focus(); field.SelectAll(); };
        return dialog.ShowDialog() == true ? field.Text : null;
    }

    internal async Task SmokeTest(string screenshot)
    {
        session.Load(Document.Create(800, 600));
        Canvas.Fit();
        Canvas.BeginPointer(new(140, 200)); Canvas.MovePointer(new(650, 390)); Canvas.EndPointer(true);
        if (!session.CanUndo || session.ActiveLayer!.Pixels.Tiles.Count == 0) throw new InvalidOperationException("UI brush gesture failed.");
        session.Undo(); session.Redo();
        LayerRotation.Text = "12"; LayerOpacity.Text = "80";
        ApplyLayer(this, new());
        Canvas.Tool = EditorTool.Move; Canvas.BeginPointer(new(0, 0)); Canvas.MovePointer(new(25, 15)); Canvas.EndPointer(true);
        if (session.ActiveLayer!.Transform.X != 25) throw new InvalidOperationException("UI move gesture failed.");
        await StabilitySmokeTest(Path.ChangeExtension(screenshot, ".checks.json"));
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(screenshot)) encoder.Save(file);
        session.MarkSaved(); Close();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (IsVisible) throw new InvalidOperationException("Clean window did not close.");
    }
}
