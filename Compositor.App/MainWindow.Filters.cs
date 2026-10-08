using System.Windows;
namespace Compositor.App;
public partial class MainWindow
{
    private void EditLensCorrection(object sender,RoutedEventArgs e)=>Safe(()=>
    {
        var dialog=new PixelFilterWindow(session,Canvas.ShowFilterPreview,PixelFilterKind.LensCorrection){Owner=this};
        try{dialog.ShowDialog();}finally{if(dialog.IsVisible)dialog.Close();Refresh();Canvas.Focus();}
    });
    private void EditAddNoise(object sender,RoutedEventArgs e)=>Safe(()=>
    {
        var dialog=new PixelFilterWindow(session,Canvas.ShowFilterPreview,PixelFilterKind.AddNoise){Owner=this};
        try{dialog.ShowDialog();}finally{if(dialog.IsVisible)dialog.Close();Refresh();Canvas.Focus();}
    });
    private void EditMotionBlur(object sender,RoutedEventArgs e)=>Safe(()=>
    {
        var dialog=new PixelFilterWindow(session,Canvas.ShowFilterPreview,PixelFilterKind.MotionBlur){Owner=this};
        try{dialog.ShowDialog();}finally{if(dialog.IsVisible)dialog.Close();Refresh();Canvas.Focus();}
    });
    private void EditGaussianBlur(object sender,RoutedEventArgs e)=>Safe(()=>
    {
        var dialog=new PixelFilterWindow(session,Canvas.ShowFilterPreview){Owner=this};
        try{dialog.ShowDialog();}finally{if(dialog.IsVisible)dialog.Close();Refresh();Canvas.Focus();}
    });
}

