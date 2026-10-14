using Compositor.Core;
using Compositor.Imaging;
using System.Windows;
namespace Compositor.App;
public partial class MainWindow
{
    private void EditRemoveBackground(object sender,RoutedEventArgs e)
    {
        if(!busy&&Canvas.IsTransforming)Canvas.CommitTransform();
        if(busy||session.InTransaction)return;
        string directory=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CompositorForWindows","Models");
        string path=System.IO.Path.Combine(directory,BackgroundModelStore.FileName);
        if(!System.IO.File.Exists(path))
        {
            var setup=new BackgroundModelSetupWindow(directory){Owner=this};if(setup.ShowDialog()!=true)return;
        }
        Safe(()=>
        {
            LayerMask Predict(Raster pixels,CancellationToken token){using var predictor=new U2NetPredictor(path);return predictor.Predict(pixels,token);}
            var dialog=new PixelFilterWindow(session,Canvas.ShowFilterPreview,PixelFilterKind.RemoveBackground,Predict){Owner=this};
            try{dialog.ShowDialog();}finally{if(dialog.IsVisible)dialog.Close();Refresh();Canvas.Focus();}
        });
    }
    private void ChangeBackgroundModel(object sender,RoutedEventArgs e)=>Safe(()=>
    {
        string directory=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CompositorForWindows","Models");
        new BackgroundModelSetupWindow(directory){Owner=this}.ShowDialog();
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






