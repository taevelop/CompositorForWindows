using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Compositor.Core;
namespace Compositor.App;
public partial class MainWindow
{
    private void SelectedAppearanceSmokeTest(string path)
    {
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        var d=Document.Create(12,12);var a=d.Layers[0] with{Opacity=.25};var b=Layer.Blank("Second",12,12) with{Opacity=.75,Visible=false};d=d with{Layers=[a,b]};
        session.Load(d);session.SelectLayers(new[]{a.Id,b.Id});
        Check(!LayerOpacity.IsEnabled&&!TransformPanel.IsEnabled&&ApplyLayerPropertiesButton.Content.ToString()=="Edit selected layers…","Multiple selection exposed single-layer property apply.");
        var dialog=new SelectedAppearanceWindow(session){Owner=this};
        try
        {
            dialog.Show();dialog.UpdateLayout();Check(ReferenceEquals(d,session.Document),"Unchecked bulk properties modified document.");
            dialog.ChangeOpacity.IsChecked=true;dialog.OpacityValue.Value=40;
            Check(session.Document.Layers.All(l=>l.Opacity==.4)&&!session.Document.Layers[1].Visible,"Opacity preview changed unchecked visibility.");
            dialog.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)dialog.ActualWidth,(int)dialog.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(dialog);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(path))png.Save(output);
            dialog.Finish(true);
        }
        finally{if(dialog.IsVisible)dialog.Finish(false);}
        Check(session.UndoCount==1&&session.SelectedLayerIds.Count==2,"Bulk apply split history or lost selection.");
        session.Undo();Check(ReferenceEquals(d,session.Document),"Bulk appearance Undo failed.");
        dialog=new SelectedAppearanceWindow(session){Owner=this};
        try
        {
            dialog.Show();dialog.ChangeBlend.IsChecked=true;dialog.BlendValue.SelectedItem=BlendMode.Screen;
            Check(session.Document.Layers.All(l=>l.Blend==BlendMode.Screen),"Bulk blend preview failed.");
            dialog.ChangeOpacity.IsChecked=true;dialog.OpacityNumber.Text="NaN";
            Check(!dialog.UpdatePreview()&&ReferenceEquals(d,session.Document),"Invalid bulk opacity did not restore original.");
            dialog.Finish(false);
        }
        finally{if(dialog.IsVisible)dialog.Finish(false);}
        Check(!session.InTransaction&&ReferenceEquals(d,session.Document),"Bulk cancellation did not restore document.");
        var group=Layer.Group("Folder",12,12);var grouped=LayerHierarchy.Wrap(d,a.Id,group);session.Load(grouped,group.Id);session.SelectLayers(new[]{group.Id,a.Id});
        dialog=new SelectedAppearanceWindow(session){Owner=this};
        try{dialog.Show();Check(!dialog.ChangeBlend.IsEnabled,"Folder selection enabled unsupported blending.");dialog.ChangeOpacity.IsChecked=true;dialog.OpacityValue.Value=50;Check(session.Document.Layers.Where(l=>session.SelectedLayerIds.Contains(l.Id)).All(l=>l.Opacity==.5),"Selected parent and child did not both receive opacity.");dialog.Finish(false);}
        finally{if(dialog.IsVisible)dialog.Finish(false);}
        session.Load(d);
    }
}