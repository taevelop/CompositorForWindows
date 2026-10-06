using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    private void PixelAdjustmentSmokeTest(string path)
    {
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        Document Source()
        {
            var d=Document.Create(64,64);var bytes=new byte[64*64*4];
            for(int p=0;p<bytes.Length;p+=4){bytes[p]=130;bytes[p+1]=80;bytes[p+2]=40;bytes[p+3]=180;}
            return d.Replace(d.Layers[0] with{Pixels=Raster.FromRgba(64,64,bytes),Transform=new(7,9,80,50,23),Opacity=.6,Mask=LayerMask.Solid(64,64,100),Effects=new(ColorOverlay:new(.1,.3,.6,.3))});
        }
        void Set(Window dialog)
        {
            switch(dialog)
            {
                case ExposureWindow w:w.SetSettings(new(1,0,1));break;
                case LevelsWindow w:w.SetSettings(new(){RGB=new(Gamma:1.5)});break;
                case CurvesWindow w:w.SetSettings(new(){RGB=new(new(0,0),new(128,180),new(255,255))});break;
                case HueSaturationWindow w:w.SetSettings(new(){Adjustments=new HueSaturationAdjustment().Adjustments.SetItem(HueRange.Master,new(Hue:60))});break;
                case ExtendedAdjustmentWindow w when dialog.Title.StartsWith("Grain"):w.SetSettings(new GrainAdjustment(50,2,70,123));break;
                case ExtendedAdjustmentWindow w:w.SetSettings(new ColorBalanceAdjustment(MidCyanRed:30));break;
                case GradientMapWindow w:w.SetSettings(new(){Shadows=new(.1,.2,.3),Highlights=new(.8,.7,.5)});break;
                case BlackWhiteWindow w:w.SetSettings(new());break;
            }
        }
        void Compare(Window dialog)
        {
            switch(dialog)
            {
                case ExposureWindow w:w.SetPreviewVisible(false);break;case LevelsWindow w:w.SetPreviewVisible(false);break;
                case CurvesWindow w:w.SetPreviewVisible(false);break;case BlackWhiteWindow w:w.SetPreviewVisible(false);break;
                case HueSaturationWindow w:w.PreviewEnabled.IsChecked=false;w.Preview();break;
                case ExtendedAdjustmentWindow w:w.PreviewEnabled.IsChecked=false;w.Preview();break;
                case GradientMapWindow w:w.PreviewEnabled.IsChecked=false;w.Preview();break;
            }
        }
        void Apply(Window dialog)
        {
            switch(dialog){case ExposureWindow w:w.ApplyEdit();break;case LevelsWindow w:w.ApplyEdit();break;case CurvesWindow w:w.ApplyEdit();break;
                case BlackWhiteWindow w:w.ApplyEdit();break;case HueSaturationWindow w:w.ApplyEdit();break;case ExtendedAdjustmentWindow w:w.ApplyEdit();break;case GradientMapWindow w:w.ApplyEdit();break;}
        }
        void Dialog(PixelAdjustmentKind kind,PixelAdjustmentEdit edit,Action<Window> action)
        {
            var dialog=CreatePixelDialog(kind,edit.PreviewSession);dialog.Owner=this;Exception? error=null;
            dialog.Loaded+=(_,_)=>{try{action(dialog);}catch(Exception e){error=e;}finally{if(dialog.IsVisible)dialog.Close();}};
            dialog.ShowDialog();if(error is not null)throw new InvalidOperationException("Pixel adjustment UI failed.",error);
        }
        foreach(var kind in Enum.GetValues<PixelAdjustmentKind>())
        {
            var doc=Source();session.Load(doc);Refresh();var source=doc.Layers[0];
            if(kind==PixelAdjustmentKind.Invert)
                ApplyPixelAdjustment(new MenuItem{Tag="Invert"},new RoutedEventArgs());
            else
            {
                using(var cancelled=new PixelAdjustmentEdit(session)){Dialog(kind,cancelled,Set);cancelled.Complete();}
                Check(ReferenceEquals(doc,session.Document)&&session.UndoCount==0,"Cancel changed source.");
                using var edit=new PixelAdjustmentEdit(session);
                Dialog(kind,edit,d=>{Set(d);Compare(d);Check(ReferenceEquals(doc,session.Document),"Pixel compare failed.");Apply(d);});edit.Complete();
            }
            Check(session.Document.Layers.Length==1&&!session.ActiveLayer!.IsAdjustment&&session.UndoCount==1,"Pixel filter created a layer or incorrect history.");
            Check(session.ActiveLayer! with{Pixels=source.Pixels}==source,"Pixel filter changed layer metadata.");
            Check(!session.ActiveLayer.Pixels.ToRgba().SequenceEqual(source.Pixels.ToRgba()),"Pixel filter did not change pixels.");
            var applied=session.Document;session.Undo();Check(ReferenceEquals(doc,session.Document),"Pixel Undo failed.");session.Redo();Check(ReferenceEquals(applied,session.Document),"Pixel Redo failed.");
        }
        var initial=Source();session.Load(initial);Refresh();
        session.Apply(d=>d.Replace(d.Layers[0] with{Name="Redo sentinel"}));session.Undo();
        using(var edit=new PixelAdjustmentEdit(session)){Dialog(PixelAdjustmentKind.Exposure,edit,Apply);edit.Complete();}
        Check(ReferenceEquals(initial,session.Document)&&session.UndoCount==0&&session.RedoCount==1,"Identity filter added history or cleared Redo.");
        using(var edit=new PixelAdjustmentEdit(session))
        {
            bool reject=true;
            edit.PreviewSession.Changed+=()=>{if(reject&&edit.PreviewSession.Document.Layers.Any(l=>l.IsAdjustment))throw new InvalidOperationException("Injected render failure");};
            Dialog(PixelAdjustmentKind.Exposure,edit,d=>{
                Compare(d);Set(d);Apply(d);
                Check(d.IsVisible&&ReferenceEquals(initial,session.Document)&&edit.PreviewSession.UndoCount==0,"Failed Apply closed editor or modified source.");
                reject=false;Apply(d);
            });edit.Complete();
        }
        Check(session.UndoCount==1,"Retry after render failure did not apply.");
        File.WriteAllText(path,JsonSerializer.Serialize(new{passed=true,checks=new[]{"nine pixel adjustments","preview/compare/cancel","single Undo/Redo","transform/mask/effects preservation","identity no-op","render-failure recovery"}}));
    }
}
