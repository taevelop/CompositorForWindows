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
    private async Task CanvasSizeSmokeTest(string path)
    {
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        session.Load(Document.Create(100,80));var original=session.Document;Exception? failure=null;
        _=Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(async ()=>
        {
            var d=Application.Current.Windows.OfType<CanvasSizeWindow>().Single();
            try
            {
                d.WidthField.Text="invalid";Check(!d.ApplyButton.IsEnabled,"Invalid dimension enabled apply.");
                d.WidthField.Text="140";d.HeightField.Text="110";
                ((ToggleButton)d.Anchors.Children[8]).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                d.FillPicker.SelectedItem="Custom";
                Exception? paletteFailure=null;
                _=Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
                {
                    var picker=Application.Current.Windows.OfType<ColorPickerWindow>().Single();
                    try{picker.SetColor(Color.FromRgb(22,55,99));picker.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
                    catch(Exception e){paletteFailure=e;picker.Close();}
                }));
                d.ColorButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(paletteFailure is not null)throw paletteFailure;
                Check(d.ReadOptions().Fill==new CanvasFill(22,55,99),"Canvas extension palette did not apply.");
                d.FillPicker.SelectedItem="White";
                Check(d.ReadOptions()==new CanvasSizeOptions(140,110,8,new(255,255,255)),"Canvas options failed.");
                d.UnitPicker.SelectedItem=CanvasUnit.Percent;
                Check(d.WidthField.Text=="140"&&d.HeightField.Text=="137.5","Percent fields failed.");
                d.Relative.IsChecked=true;d.Relative.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(d.WidthField.Text=="40"&&d.HeightField.Text=="37.5","Relative fields failed.");
                d.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)d.ActualWidth,(int)d.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(d);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
                using(var output=File.Create(path))png.Save(output);
                d.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await d.PendingApply;
            }
            catch(Exception e){failure=e;d.Close();}
        }));
        ChangeCanvasSize(null,new());
        if(failure is not null)throw failure;
        Check(session.Document.Width==140&&session.Document.Height==110&&session.Document.Layers.Length==2,"Canvas size menu did not apply.");
        Check(session.Document.Layers[1].Transform.X==40&&session.Document.Layers[1].Transform.Y==30,"Canvas anchor did not apply.");
        Check(session.UndoCount==1,"Canvas size must be a single Undo.");
        var resized=session.Document;session.Undo();Check(ReferenceEquals(original,session.Document),"Canvas Undo failed.");
        session.Redo();Check(ReferenceEquals(resized,session.Document),"Canvas Redo failed.");
        _=Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
        {
            var d=Application.Current.Windows.OfType<CanvasSizeWindow>().Single();d.WidthField.Text="160";d.Close();
        }));
        ChangeCanvasSize(null,new());Check(ReferenceEquals(resized,session.Document),"Cancel changed the canvas.");
        Task cancelled=Task.CompletedTask;
        _=Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
        {
            var d=Application.Current.Windows.OfType<CanvasSizeWindow>().Single();
            d.WidthField.Text="2000";d.HeightField.Text="2000";d.FillPicker.SelectedItem="White";
            d.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));cancelled=d.PendingApply;d.Close();
        }));
        ChangeCanvasSize(null,new());await cancelled;
        Check(ReferenceEquals(resized,session.Document),"Late resize result applied after cancellation.");
    }
}
