using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    private async Task ImageSizeSmokeTest(string path)
    {
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        var doc=Document.Create(20,10);doc=doc.Replace(doc.Layers[0] with{Pixels=LayerMask.Solid(20,10,100).Pixels,Mask=LayerMask.Solid(1,1,160)});
        session.Load(doc);Exception? failure=null;
        _=Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(async ()=>
        {
            var d=Application.Current.Windows.OfType<ImageSizeWindow>().Single();
            try
            {
                d.WidthField.Text="bad";Check(!d.ApplyButton.IsEnabled,"Invalid image size enabled apply.");
                d.WidthField.Text="40";Check(d.HeightField.Text=="20","Image aspect lock failed.");
                d.ResolutionField.Text="144";d.SamplingPicker.SelectedItem=Sampling.Nearest;
                d.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)d.ActualWidth,(int)d.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(d);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
                using(var output=File.Create(path))png.Save(output);
                d.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await d.PendingApply;
            }
            catch(Exception e){failure=e;d.Close();}
        }));
        ChangeImageSize(null,new());if(failure is not null)throw failure;
        var resized=session.Document;
        Check(resized.Width==40&&resized.Height==20&&resized.Resolution==144&&resized.Layers[0].Pixels.Width==40,"Image resize menu failed.");
        Check(session.UndoCount==1,"Image resize must be one Undo.");session.Undo();Check(ReferenceEquals(doc,session.Document),"Image resize Undo failed.");
        session.Redo();Check(ReferenceEquals(resized,session.Document),"Image resize Redo failed.");
        _=Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(async ()=>
        {
            var d=Application.Current.Windows.OfType<ImageSizeWindow>().Single();
            try
            {
                d.Resample.IsChecked=false;d.Resample.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(d.Units.Items.Count==2&&!d.Locked.IsEnabled&&d.SamplingPanel.Visibility==Visibility.Collapsed,"Resolution-only controls failed.");
                d.ResolutionField.Text="300";d.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await d.PendingApply;
            }
            catch(Exception e){failure=e;d.Close();}
        }));
        ChangeImageSize(null,new());if(failure is not null)throw failure;
        Check(session.Document.Resolution==300&&ReferenceEquals(resized.Layers[0],session.Document.Layers[0]),"Resolution-only resize changed pixels.");
        var beforeCancel=session.Document;Task pending=Task.CompletedTask;
        _=Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
        {
            var d=Application.Current.Windows.OfType<ImageSizeWindow>().Single();
            d.WidthField.Text="2000";d.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));pending=d.PendingApply;d.Close();
        }));
        ChangeImageSize(null,new());await pending;Check(ReferenceEquals(beforeCancel,session.Document),"Late image resize changed document after cancel.");
    }
}
