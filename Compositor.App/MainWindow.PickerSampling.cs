using System.Windows;
namespace Compositor.App;
public partial class MainWindow
{
    internal bool SamplePickerColor(ColorPickerWindow picker)
    {
        if(busy||Canvas.HasInteraction)return false;
        var original=picker.SelectedColor;bool accepted=false;
        try
        {
            var surface=new CanvasColorSampleWindow(picker,Canvas,original){PreviewColor=picker.SetColor};
            if(surface.ShowDialog()!=true||surface.Sampled is not {} color)return false;
            picker.SetColor(color);accepted=true;return true;
        }
        finally{if(!accepted)picker.SetColor(original);}
    }
}
