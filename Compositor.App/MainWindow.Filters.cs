using Compositor.Core;
using Compositor.Imaging;
using System.Windows;
namespace Compositor.App;
public partial class MainWindow
{
    private void EditRemoveBackground(object sender,RoutedEventArgs e)=>Safe(()=>
    {
        var chooser=new Microsoft.Win32.OpenFileDialog{Title="Choose the verified U2Net background model",Filter="U2Net ONNX model|*.onnx"};
        if(chooser.ShowDialog(this)!=true)return;
        string path=chooser.FileName;
        LayerMask Predict(Raster pixels,CancellationToken token){using var predictor=new U2NetPredictor(path);return predictor.Predict(pixels,token);}
        var dialog=new PixelFilterWindow(session,Canvas.ShowFilterPreview,PixelFilterKind.RemoveBackground,Predict){Owner=this};
        try{dialog.ShowDialog();}finally{if(dialog.IsVisible)dialog.Close();Refresh();Canvas.Focus();}
    });
    private void EditLensCorrection(object sender,RoutedEventArgs e)=>Safe(()=>
    {
        var dialog=new PixelFilterWindow(session,Canvas.ShowFilterPreview,PixelFilterKind.LensCorrection){Owner=this};
        try{dialog.ShowDialog();}finally{if(dialog.IsVisible)dialog.Close();Refresh();Canvas.Focus();}
    });
    private void EditContentAwareFill(object sender,RoutedEventArgs e)=>Safe(()=>
    {
        var dialog=new PixelFilterWindow(session,Canvas.ShowFilterPreview,PixelFilterKind.ContentAwareFill){Owner=this};
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



