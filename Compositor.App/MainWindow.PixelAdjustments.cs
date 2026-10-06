using System.Windows;
using System.Windows.Controls;
using Compositor.Core;
namespace Compositor.App;

internal enum PixelAdjustmentKind { HueSaturation, Exposure, Levels, Curves, Invert, BlackWhite, ColorBalance, Grain, GradientMap }
public partial class MainWindow
{
    internal static Window CreatePixelDialog(PixelAdjustmentKind kind, EditorSession preview)
    {
        Window window = kind switch
        {
            PixelAdjustmentKind.HueSaturation => new HueSaturationWindow(preview, true, true),
            PixelAdjustmentKind.Exposure => new ExposureWindow(preview, true, true),
            PixelAdjustmentKind.Levels => new LevelsWindow(preview, true, true),
            PixelAdjustmentKind.Curves => new CurvesWindow(preview, true, true),
            PixelAdjustmentKind.BlackWhite => new BlackWhiteWindow(preview, true, true),
            PixelAdjustmentKind.ColorBalance => new ExtendedAdjustmentWindow(preview, false, true, true),
            PixelAdjustmentKind.Grain => new ExtendedAdjustmentWindow(preview, true, true, true),
            PixelAdjustmentKind.GradientMap => new GradientMapWindow(preview, true, true),
            _ => throw new ArgumentException("This adjustment has no editor.")
        };
        window.Title += " — image pixels";
        return window;
    }
    private void ApplyPixelAdjustment(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (sender is not MenuItem { Tag: string tag } || !Enum.TryParse<PixelAdjustmentKind>(tag, out var kind)) return;
        using var edit = new PixelAdjustmentEdit(session);
        try
        {
            if (kind == PixelAdjustmentKind.Invert)
                edit.PreviewSession.Apply(d => d with { Layers = d.Layers.Add(Layer.InvertLayer(d.Width, d.Height)) });
            else
            {
                var dialog = CreatePixelDialog(kind, edit.PreviewSession); dialog.Owner = this;
                try { dialog.ShowDialog(); } finally { if (dialog.IsVisible) dialog.Close(); }
            }
            edit.Complete();
        }
        finally { Refresh(); Canvas.Focus(); }
    });
}
