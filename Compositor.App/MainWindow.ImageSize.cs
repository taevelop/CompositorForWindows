using System.Windows;
namespace Compositor.App;
public partial class MainWindow
{
    private void ChangeImageSize(object? sender,RoutedEventArgs e)=>Safe(()=>
    {
        Canvas.CancelInteraction();Canvas.CancelCrop();var original=session.Document;
        var dialog=new ImageSizeWindow(original){Owner=this};
        if(dialog.ShowDialog()==true&&dialog.ResultDocument is {} next)
        {
            if(!ReferenceEquals(original,session.Document))throw new InvalidOperationException("The document changed while resizing.");
            session.Apply(_=>next);Canvas.Fit();
        }
    });
}
