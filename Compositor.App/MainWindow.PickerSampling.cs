using System.Windows;
namespace Compositor.App;
public partial class MainWindow
{
    internal bool SamplePickerColor(ColorPickerWindow picker)
    {
        if(busy||Canvas.HasInteraction)return false;
        var surface=new CanvasColorSampleWindow(picker,Canvas);
        if(surface.ShowDialog()!=true||surface.Sampled is not {} color)return false;
        picker.SetColor(color);return true;
    }
}
