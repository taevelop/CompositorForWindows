using System.Windows;
using System.Windows.Controls;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    private GradientFillSettings ReadGradient(PointD start,PointD end)
    {
        ColorPickerWindow.TryHex(BrushColor.Text,out var foreground);ColorPickerWindow.TryHex(backgroundColor,out var background);
        if(session.EditMask){byte value=(byte)Math.Round(MaskSlider.Value*255/100);foreground=System.Windows.Media.Color.FromRgb(value,value,value);value=(byte)(255-value);background=System.Windows.Media.Color.FromRgb(value,value,value);}
        return new(start,end,foreground.R,foreground.G,foreground.B,background.R,background.G,background.B,
            GradientRadial.IsChecked==true?GradientShape.Radial:GradientShape.Linear,
            GradientTransparent.IsChecked==true?GradientStyle.ForegroundToTransparent:GradientStyle.ForegroundToBackground,
            GradientReverse.IsChecked==true,GradientOpacity.Value/100);
    }
    private void GradientChanged(object sender,RoutedEventArgs e){if(IsInitialized)Canvas?.UpdateGradient();}
    private async void ApplyGradient(object? sender,RoutedEventArgs e){await Canvas.CommitGradientAsync();Refresh();}
    private void CancelGradient(object? sender,RoutedEventArgs e){Canvas.CancelGradient();Refresh();}
}
