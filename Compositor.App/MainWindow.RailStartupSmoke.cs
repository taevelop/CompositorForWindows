using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Compositor.App;

public partial class MainWindow
{
    internal async Task RailStartupSmoke(string reportPath)
    {
        // Observe the normal WPF layout cycle. UpdateLayout or resizing before
        // this check would conceal the reported first-show scrollbar defect.
        var rows = new List<object>();
        async Task Observe(MainWindow window, string name, bool overflow)
        {
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            var rail = window.ToolRailScroll;
            bool visible = rail.ComputedVerticalScrollBarVisibility == Visibility.Visible;
            if (visible != overflow || (rail.ScrollableHeight > .1) != overflow)
                throw new InvalidOperationException($"{name}: rail extent {rail.ExtentHeight}, viewport {rail.ViewportHeight}, scroll {rail.ScrollableHeight}, scrollbar {rail.ComputedVerticalScrollBarVisibility}.");
            if (!overflow && window.ColorButton.TranslatePoint(new Point(0, window.ColorButton.ActualHeight), rail).Y > rail.ActualHeight + 1)
                throw new InvalidOperationException($"{name}: palette is clipped.");
            rows.Add(new { name, window.ActualWidth, window.ActualHeight, rail.ExtentHeight, rail.ViewportHeight, rail.ScrollableHeight, scrollbar = rail.ComputedVerticalScrollBarVisibility.ToString(), tools = window.ToolPicker.Items.Count, dpi = VisualTreeHelper.GetDpi(window).DpiScaleY });
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(reportPath + "." + name + ".png"); encoder.Save(output);
        }

        await Observe(this, "first-show", false);
        var maximized = new MainWindow { ShowInTaskbar = false, Left = -10000, Top = -10000, WindowState = WindowState.Maximized };
        try
        {
            maximized.Show();
            await Observe(maximized, "first-show-maximized", false);
        }
        finally { maximized.CloseSmokeOnFailure(); }

        Width = 1000; Height = 650;
        await Observe(this, "compact", true);
        ToolRailScroll.ScrollToBottom();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        if (ColorButton.TranslatePoint(new Point(0, ColorButton.ActualHeight), ToolRailScroll).Y > ToolRailScroll.ActualHeight + 1)
            throw new InvalidOperationException("Compact rail cannot scroll to its palette.");
        Width = 1320; Height = 860;
        await Observe(this, "restored", false);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { passed = true, rows, note = "Natural first-show layout on the development PC; no forced UpdateLayout before observations. Other devices and DPI configurations are not certified." }, new JsonSerializerOptions { WriteIndented = true }));
        CloseSmokeOnFailure();
    }
}
