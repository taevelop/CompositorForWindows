using System.Windows;
using System.Windows.Media;
namespace Compositor.App;
public partial class MainWindow
{
    private void ChangeCanvasSize(object? sender,RoutedEventArgs e)=>Safe(()=>
    {
        Canvas.CancelInteraction();Canvas.CancelCrop();
        var original=session.Document;
        ColorPickerWindow.TryHex(BrushColor.Text,out var foreground);
        ColorPickerWindow.TryHex(backgroundColor,out var background);
        var dialog=new CanvasSizeWindow(original,foreground,background){Owner=this};
        if(dialog.ShowDialog()==true&&dialog.ResultDocument is {} next)
        {
            if(!ReferenceEquals(original,session.Document))throw new InvalidOperationException("The document changed while resizing.");
            session.Apply(_=>next);Canvas.Fit();
        }
    });
}
