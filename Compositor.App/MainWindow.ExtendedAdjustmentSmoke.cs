using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    private void ExtendedAdjustmentSmokeTest(string path)
    {
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        foreach(bool grain in new[]{false,true})
        {
            var doc=Document.Create(128,128);var paint=new BrushStroke(doc.Layers[0],new(100,1,1,100,120,160),128,128);paint.Append(new(64,64));
            doc=doc.Replace(doc.Layers[0] with {Pixels=paint.Pixels});session.Load(doc);Refresh();
            void Dialog(bool create,Action<ExtendedAdjustmentWindow> action)
            {
                var d=new ExtendedAdjustmentWindow(session,grain,create){Owner=this};Exception? error=null;
                d.Loaded+=(_,_)=>{try{action(d);}catch(Exception e){error=e;}finally{if(d.IsVisible)d.Close();}};
                try{d.ShowDialog();}finally{d.CancelEdit();Refresh();}
                if(error is not null)throw new InvalidOperationException("Extended adjustment UI failed.",error);
            }
            Dialog(true,d=>{if(grain)d.SetSettings(new GrainAdjustment(60,2,70,123));else d.SetSettings(new ColorBalanceAdjustment(MidCyanRed:30));});
            Check(ReferenceEquals(doc,session.Document),"Create cancel modified document.");
            Dialog(true,d=>{if(grain)d.SetSettings(new GrainAdjustment(60,2,70,123));else d.SetSettings(new ColorBalanceAdjustment(MidCyanRed:30));d.PreviewEnabled.IsChecked=false;d.Preview();Check(ReferenceEquals(doc,session.Document),"Compare failed.");d.ApplyEdit();});
            Check(session.UndoCount==1 && session.ActiveLayer!.IsAdjustment,"Apply not one adjustment edit.");
            var applied=session.Document;session.Undo();Check(ReferenceEquals(doc,session.Document),"Undo failed.");session.Redo();Check(ReferenceEquals(applied,session.Document),"Redo failed.");
            int undo=session.UndoCount;Dialog(false,d=>d.ApplyEdit());Check(session.UndoCount==undo,"No-op changed history.");
            Dialog(false,d=>{
                if(grain){d.SeedInput.Text="4294967296";Check(!d.Preview(),"Invalid seed accepted.");d.SeedInput.Text="123";d.Preview();}
                d.UpdateLayout();var bitmap=new RenderTargetBitmap((int)d.ActualWidth,(int)d.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(d);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.ChangeExtension(path,grain?".grain.png":".balance.png"));png.Save(file);
            });
            Check(ReferenceEquals(applied,session.Document),"Edit cancel changed document.");
        }
        File.WriteAllText(path,JsonSerializer.Serialize(new{passed=true,checks=new[]{"create/edit/cancel","compare/apply","undo/redo/no-op","uint seed validation","both dialog renders"}}));
    }
}
