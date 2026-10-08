using System.Windows;
namespace Compositor.App;
public partial class MainWindow
{
    private void EditGaussianBlur(object sender,RoutedEventArgs e)=>Safe(()=>
    {
        var dialog=new GaussianBlurWindow(session,Canvas.ShowFilterPreview){Owner=this};
        try{dialog.ShowDialog();}finally{if(dialog.IsVisible)dialog.Close();Refresh();Canvas.Focus();}
    });
}
