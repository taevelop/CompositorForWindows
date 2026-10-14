using Compositor.Core;
using Compositor.Imaging;
using System.Windows;
namespace Compositor.App;
public partial class MainWindow
{
    private async void EditRemoveBackground(object sender,RoutedEventArgs e)
    {
        if(!busy&&Canvas.IsTransforming)Canvas.CommitTransform();
        if(busy||session.InTransaction)return;
        string directory=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CompositorForWindows","Models");
        string path=System.IO.Path.Combine(directory,BackgroundModelStore.FileName);
        if(!System.IO.File.Exists(path))
        {
            var chooser=new Microsoft.Win32.OpenFileDialog{Title="Install a background removal model (one-time setup)",Filter="U2Net ONNX model|*.onnx"};
            if(chooser.ShowDialog(this)!=true)return;
            if(!await Work("Installing background removal model…",()=>BackgroundModelStore.Install(chooser.FileName,directory)))return;
        }
        Safe(()=>
        {
            LayerMask Predict(Raster pixels,CancellationToken token){using var predictor=new U2NetPredictor(path);return predictor.Predict(pixels,token);}
            var dialog=new PixelFilterWindow(session,Canvas.ShowFilterPreview,PixelFilterKind.RemoveBackground,Predict){Owner=this};
            try{dialog.ShowDialog();}finally{if(dialog.IsVisible)dialog.Close();Refresh();Canvas.Focus();}
        });
    }
    private async void ChangeBackgroundModel(object sender,RoutedEventArgs e)
    {
        if(!busy&&Canvas.IsTransforming)Canvas.CommitTransform();if(busy||session.InTransaction)return;
        var chooser=new Microsoft.Win32.OpenFileDialog{Title="Change background removal model",Filter="U2Net ONNX model|*.onnx"};
        if(chooser.ShowDialog(this)!=true)return;
        string directory=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CompositorForWindows","Models");
        await Work("Installing background removal model…",()=>BackgroundModelStore.Install(chooser.FileName,directory));
    }
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





