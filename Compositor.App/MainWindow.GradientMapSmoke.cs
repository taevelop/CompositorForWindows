using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    private void GradientMapSmokeTest(string path)
    {
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        var doc=Document.Create(128,128);var paint=new BrushStroke(doc.Layers[0],new(100,1,1,100,120,160),128,128);paint.Append(new(64,64));
        doc=doc.Replace(doc.Layers[0] with {Pixels=paint.Pixels});session.Load(doc);Refresh();
        void Dialog(bool create,Action<GradientMapWindow> action)
        {
            var d=new GradientMapWindow(session,create){Owner=this};Exception? error=null;
            d.Loaded+=(_,_)=>{try{action(d);}catch(Exception e){error=e;}finally{if(d.IsVisible)d.Close();}};
            try{d.ShowDialog();}finally{d.CancelEdit();Refresh();}
            if(error is not null)throw new InvalidOperationException("Gradient Map UI failed.",error);
        }
        var settings=new GradientMapAdjustment{Shadows=new(.1,.2,.3),Highlights=new(.9,.7,.5),Reversed=true};
        Dialog(true,d=>d.SetSettings(settings));Check(ReferenceEquals(doc,session.Document),"Create cancel failed.");
        Dialog(true,d=>{
            d.SetSettings(settings);
            Exception? pickerError=null;
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>{
                var picker=Application.Current.Windows.OfType<ColorPickerWindow>().Single();
                try{picker.SetColor(Colors.Blue);picker.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
                catch(Exception e){pickerError=e;if(picker.IsVisible)picker.Close();}
            }));
            d.ShadowsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(pickerError is not null)throw pickerError;
            Check(session.ActiveLayer?.GradientMap?.Shadows==new AdjustmentColor(0,0,1),"Color picker did not update gradient.");
            d.SetSettings(settings);d.PreviewEnabled.IsChecked=false;d.Preview();Check(ReferenceEquals(doc,session.Document),"Compare failed.");d.ApplyEdit();
        });
        Check(session.UndoCount==1 && session.ActiveLayer?.GradientMap==settings,"Apply failed.");
        var applied=session.Document;session.Undo();Check(ReferenceEquals(doc,session.Document),"Undo failed.");session.Redo();Check(ReferenceEquals(applied,session.Document),"Redo failed.");
        int undo=session.UndoCount;Dialog(false,d=>d.ApplyEdit());Check(session.UndoCount==undo,"No-op created history.");
        Dialog(false,d=>{
            d.UpdateLayout();var image=new RenderTargetBitmap((int)d.ActualWidth,(int)d.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(d);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var output=File.Create(Path.ChangeExtension(path,".png"));encoder.Save(output);
            d.SetSettings(new());});
        Check(ReferenceEquals(applied,session.Document),"Edit cancel failed.");
        File.WriteAllText(path,JsonSerializer.Serialize(new{passed=true,checks=new[]{"palette selection","reverse","create/edit/cancel","compare/apply","undo/redo/no-op","dialog render"}}));
    }
}
