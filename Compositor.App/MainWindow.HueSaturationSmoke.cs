using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Compositor.Core;
namespace Compositor.App;
public partial class MainWindow
{
    private void HueSaturationSmokeTest(string path)
    {
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        var doc=Document.Create(128,128);var rgba=new byte[128*128*4];
        for(int y=0;y<128;y++)for(int x=0;x<128;x++){int p=(y*128+x)*4;rgba[p+(x<64?0:2)]=255;rgba[p+3]=255;}
        doc=doc.Replace(doc.Layers[0] with{Pixels=Raster.FromRgba(128,128,rgba)});session.Load(doc);Refresh();
        void Dialog(bool create,Action<HueSaturationWindow> action,bool capture=false)
        {
            var d=new HueSaturationWindow(session,create){Owner=this};Exception? error=null;
            d.Loaded+=async(_,_)=>{
                try{
                    await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);action(d);
                    if(capture){
                        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);d.UpdateLayout();
                        var bitmap=new RenderTargetBitmap((int)d.ActualWidth,(int)d.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(d);
                        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.ChangeExtension(path,".png"));encoder.Save(output);
                    }
                }catch(Exception e){error=e;}finally{if(d.IsVisible)d.Close();}
            };
            try{d.ShowDialog();}finally{d.CancelEdit();Refresh();}
            if(error is not null)throw new InvalidOperationException("Hue/Saturation UI failed.",error);
        }
        Dialog(true,d=>{d.Sliders[0].Value=120;d.Preview();});Check(ReferenceEquals(doc,session.Document),"Create cancel failed.");
        Dialog(true,d=>{
            d.RangePicker.SelectedItem=HueRange.Greens;d.Sliders[0].Value=30;
            d.RangePicker.SelectedItem=HueRange.Master;Check(d.Sliders[0].Value==0,"Range values leaked.");
            d.RangePicker.SelectedItem=HueRange.Greens;Check(d.Sliders[0].Value==30,"Range value lost.");
            d.SetSettings(new(){Range=HueRange.Greens});d.SetMode(HueInputMode.Sample);
            var red=d.Image.ViewPoint(new(20,20));var blue=d.Image.ViewPoint(new(100,20));
            Check(d.Image.BeginInteraction(MouseButton.Left,red),"Sample did not start.");
            Check(!d.Image.FinishInteraction(MouseButton.Right,red,false),"Wrong button ended sample.");
            d.Image.FinishInteraction(MouseButton.Left,red,false);
            Check(d.Settings.Band(HueRange.Greens).Weight(0)==1,"Sample did not recenter.");
            d.SetMode(HueInputMode.Add);d.Image.BeginInteraction(MouseButton.Left,blue);d.Image.FinishInteraction(MouseButton.Left,blue,false);
            Check(d.Settings.Band(HueRange.Greens).Weight(240)==1,"Add did not include blue.");
            d.SetMode(HueInputMode.Remove);d.Image.BeginInteraction(MouseButton.Left,blue);d.Image.FinishInteraction(MouseButton.Left,blue,false);
            Check(d.Settings.Band(HueRange.Greens).Weight(240)==0,"Remove did not exclude blue.");
            d.Colorize.IsChecked=true;d.Colorize.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(d.Settings.Colorize&&d.Sliders[1].Value==25&&!d.RangePicker.IsEnabled,"Colorize defaults failed.");
            d.Colorize.IsChecked=false;d.Colorize.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(d.Settings.IsIdentity&&d.RangePicker.IsEnabled,"Colorize off did not reset.");
            d.SetSettings(new());var before=d.Settings;d.SetMode(HueInputMode.Target);
            Check(d.Image.BeginInteraction(MouseButton.Left,red),"Target did not start.");d.Image.MoveInteraction(new(red.X+40,red.Y),false);
            Check(d.Settings.Adjustment(HueRange.Reds).Saturation==20,"Target saturation failed.");
            d.Image.MoveInteraction(new(red.X+40,red.Y),true);Check(d.Settings.Adjustment(HueRange.Reds).Hue==20&&d.Settings.Adjustment(HueRange.Reds).Saturation==20,"Ctrl target hue or retained saturation failed.");
            d.Image.CancelGesture();Check(d.Settings==before,"Target cancel failed.");
            d.Image.BeginInteraction(MouseButton.Left,red);d.Image.FinishInteraction(MouseButton.Left,new(red.X+120,red.Y),true);
            Check(d.Settings.Adjustment(HueRange.Reds).Hue==60,"Target commit failed.");
            d.PreviewEnabled.IsChecked=false;d.Preview();Check(ReferenceEquals(doc,session.Document),"Compare failed.");d.ApplyEdit();
        });
        Check(session.UndoCount==1&&session.ActiveLayer?.HueSaturation is not null,"Apply not one history entry.");
        var applied=session.Document;session.Undo();Check(ReferenceEquals(doc,session.Document),"Undo failed.");session.Redo();Check(ReferenceEquals(applied,session.Document),"Redo failed.");
        int undo=session.UndoCount;Dialog(false,d=>d.ApplyEdit());Check(session.UndoCount==undo,"No-op created history.");
        Dialog(false,d=>{
            d.RangePicker.SelectedItem=HueRange.Reds;
            var before=d.Settings.Band(HueRange.Reds);
            d.Spectrum.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(d),Environment.TickCount,Key.Right){RoutedEvent=Keyboard.KeyDownEvent});
            Check(d.Settings.Band(HueRange.Reds).FalloffStart==HueBand.Wrap(before.FalloffStart+1),"Spectrum keyboard handle failed.");
            d.Preview();
        },true);
        Check(ReferenceEquals(applied,session.Document),"Edit cancel failed.");
        var legacy=Layer.HueSaturationLayer(128,128) with{HueSaturation=new(300,-20,3)};
        session.Load(doc with{Layers=doc.Layers.Add(legacy)});session.ActiveLayerId=legacy.Id;
        Dialog(false,d=>{Check(d.Sliders[0].Value==300,"Imported legacy hue clamped.");d.ApplyEdit();});
        Check(session.ActiveLayer!.HueSaturation==legacy.HueSaturation&&session.UndoCount==0,"No-op changed legacy settings.");
        File.WriteAllText(path,JsonSerializer.Serialize(new{passed=true,checks=new[]{"ranges/sliders","spectrum handle","sample/add/remove","target and Ctrl drag","gesture cancel/button pairing","create/edit/cancel","compare/apply","undo/redo/no-op","dialog render"}}));
    }
}
