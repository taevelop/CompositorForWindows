using System.Windows;
using System.Windows.Media;
namespace Compositor.App;
public partial class MainWindow
{
    private string backgroundColor="#FFFFFF";
    private void SetBackgroundColor(string value)
    {
        if(!ColorPickerWindow.TryHex(value,out var color))return;
        backgroundColor=ColorPickerWindow.Hex(color);
        BackgroundColorSwatch.Background=new SolidColorBrush(color);
        BackgroundColorButton.ToolTip="Background color · "+backgroundColor;RefreshPaletteSwatches();
    }
    private void RefreshPaletteSwatches()
    {
        if(session.EditMask)
        {
            byte value=(byte)Math.Round(MaskSlider.Value*255/100);
            ColorSwatch.Background=new SolidColorBrush(Color.FromRgb(value,value,value));
            byte inverse=(byte)(255-value);BackgroundColorSwatch.Background=new SolidColorBrush(Color.FromRgb(inverse,inverse,inverse));
        }
        else
        {
            if(ColorPickerWindow.TryHex(BrushColor.Text,out var foreground))ColorSwatch.Background=new SolidColorBrush(foreground);
            if(ColorPickerWindow.TryHex(backgroundColor,out var background))BackgroundColorSwatch.Background=new SolidColorBrush(background);
        }
    }    private void PickBackgroundColor(object sender,RoutedEventArgs e)
    {
        if(busy||session.InTransaction||session.EditMask)return;
        ColorPickerWindow.TryHex(backgroundColor,out var color);
        var dialog=new ColorPickerWindow(color){Owner=this};
        if(dialog.ShowDialog()==true)SetBackgroundColor(ColorPickerWindow.Hex(dialog.SelectedColor));
        Canvas.Focus();
    }
    private void SwapPalette(object? sender,RoutedEventArgs e)
    {
        if(busy||session.InTransaction)return;
        if(session.EditMask){MaskSlider.Value=100-MaskSlider.Value;return;}
        var foreground=BrushColor.Text;BrushColor.Text=backgroundColor;SetBackgroundColor(foreground);
    }
    private void ResetPalette(object? sender,RoutedEventArgs e)
    {
        if(busy||session.InTransaction)return;
        if(session.EditMask){MaskSlider.Value=0;return;}
        BrushColor.Text="#000000";SetBackgroundColor("#FFFFFF");
    }
}
