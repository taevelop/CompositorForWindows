using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    private async Task FilterSmokeTest(string reportPath)
    {
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        var saved=session.Document;var active=session.ActiveLayerId;bool editMask=session.EditMask;
        string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"filter-smoke-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        GaussianBlurWindow? dialog=null;
        try
        {
            var document=Document.Create(64,64);var layer=document.Layers[0] with{Pixels=ShapeRaster.Create(new(ShapeKind.Rectangle,.2,.4,.7),32,32),Transform=new(8,8,32,32),Mask=LayerMask.Solid(32,32)};
            document=document.Replace(layer);session.Load(document);Canvas.Fit();
            dialog=new(session,Canvas.ShowFilterPreview){Owner=this};dialog.Show();dialog.Radius.Value=2;dialog.Radius.Value=4;await dialog.Pending;
            Check(dialog.ApplyButton.IsEnabled&&session.InTransaction&&session.UndoCount==0,"Blur preview did not finish privately.");
            var expected=await Task.Run(()=>GaussianBlur.Apply(document,layer.Id,4));
            Check(ReferenceEquals(session.Document,document)&&CompositePixels(expected).SequenceEqual(CompositePixels(Canvas.DisplayDocument)),"Latest blur settings were not shown or mutated the document.");
            dialog.PreviewEnabled.IsChecked=false;Check(ReferenceEquals(session.Document,document),"Blur comparison did not restore source.");
            dialog.PreviewEnabled.IsChecked=true;Check(!ReferenceEquals(Canvas.DisplayDocument,document),"Blur comparison did not restore preview.");
            UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth),(int)Math.Ceiling(dialog.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(dialog);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.ChangeExtension(reportPath,".png")))encoder.Save(file);
            await dialog.ApplyEditAsync();dialog=null;
            Check(!session.InTransaction&&session.UndoCount==1,"Blur did not commit exactly once.");
            var output=CompositePixels(session.Document);Undo(this,new());Check(ReferenceEquals(session.Document,document),"Blur Undo did not restore source.");Redo(this,new());
            projectPath=Path.Combine(root,"Blur.comp");Check(await Save(false)&&await ReopenSavedProject(projectPath),"Blur UI save/reopen failed.");
            Check(output.SequenceEqual(CompositePixels(session.Document))&&session.ActiveLayer!.Mask!.Pixels.Width==32,"Blur save/reopen lost pixels or mask grid.");
            var before=session.Document;dialog=new(session,Canvas.ShowFilterPreview){Owner=this};dialog.Show();dialog.Radius.Value=8;var cancelled=dialog.Pending;dialog.Close();dialog=null;await cancelled;
            Check(ReferenceEquals(before,session.Document)&&!session.InTransaction&&!session.CanUndo,"Cancelled blur published a result.");
            dialog=new(session,Canvas.ShowFilterPreview){Owner=this};dialog.Show();dialog.Radius.Value=8;var stale=dialog.Pending;session.Load(before);session.Begin();await stale;dialog.Close();dialog=null;
            Check(session.InTransaction&&ReferenceEquals(before,session.Document),"Stale blur closed a newer transaction.");session.Cancel();
            var large=Document.Create(3000,64);var wide=large.Layers[0] with{Pixels=ShapeRaster.Create(new(ShapeKind.Rectangle,.2,.4,.7),2600,32),Transform=new(10,10,2600,32)};
            large=large.Replace(wide);session.Load(large);dialog=new(session,Canvas.ShowFilterPreview){Owner=this};dialog.Show();dialog.Radius.Value=4;await dialog.Pending;
            Check(ReferenceEquals(session.Document,large)&&Canvas.DisplayDocument.Layers[0].Pixels.Width==2048,"Large filter preview entered the document or exceeded its cap.");
            var fullExpected=await Task.Run(()=>GaussianBlur.Apply(large,wide.Id,4));await dialog.ApplyEditAsync();dialog=null;
            Check(session.ActiveLayer!.Pixels.Width>2048&&session.UndoCount==1&&CompositePixels(fullExpected).SequenceEqual(CompositePixels(session.Document)),"Apply used preview pixels instead of full-resolution blur.");
            Undo(this,new());Check(ReferenceEquals(session.Document,large)&&ReferenceEquals(Canvas.DisplayDocument,large),"Full-resolution blur Undo retained display pixels.");
            session.Load(large);dialog=new(session,Canvas.ShowFilterPreview){Owner=this};dialog.Show();dialog.Radius.Value=4;await dialog.Pending;
            var applying=dialog.ApplyEditAsync();dialog.Close();dialog=null;await applying;
            Check(ReferenceEquals(session.Document,large)&&!session.InTransaction&&!session.CanUndo&&ReferenceEquals(Canvas.DisplayDocument,large),"Closing during full-size Apply published pixels or retained preview.");
            session.Load(document);dialog=new(session,Canvas.ShowFilterPreview,true){Owner=this};dialog.Show();
            Check(dialog.Title=="Motion Blur"&&dialog.Radius.Minimum==1&&dialog.Radius.Maximum==2000,"Motion distance controls have wrong range.");
            dialog.Radius.Value=16;dialog.Angle.Value=45;await dialog.Pending;
            var motionExpected=await Task.Run(()=>MotionBlur.Apply(document,layer.Id,16,45));
            Check(dialog.ApplyButton.IsEnabled&&ReferenceEquals(session.Document,document)&&CompositePixels(motionExpected).SequenceEqual(CompositePixels(Canvas.DisplayDocument)),"Motion preview settings or document immutability failed.");
            UpdateLayout();var motionBitmap=new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth),(int)Math.Ceiling(dialog.ActualHeight),96,96,PixelFormats.Pbgra32);motionBitmap.Render(dialog);
            var motionPng=new PngBitmapEncoder();motionPng.Frames.Add(BitmapFrame.Create(motionBitmap));using(var file=File.Create(Path.ChangeExtension(reportPath,".motion.png")))motionPng.Save(file);
            await dialog.ApplyEditAsync();dialog=null;Check(session.UndoCount==1&&!session.InTransaction,"Motion blur did not commit one edit.");
            var motionOutput=CompositePixels(session.Document);Undo(this,new());Check(ReferenceEquals(session.Document,document),"Motion Undo failed.");Redo(this,new());
            projectPath=Path.Combine(root,"Motion.comp");Check(await Save(false)&&await ReopenSavedProject(projectPath)&&motionOutput.SequenceEqual(CompositePixels(session.Document)),"Motion save/reopen changed pixels.");
            var motionBefore=session.Document;dialog=new(session,Canvas.ShowFilterPreview,true){Owner=this};dialog.Show();dialog.Radius.Value=100;dialog.Angle.Value=-45;var motionCancel=dialog.Pending;dialog.Close();dialog=null;await motionCancel;
            Check(ReferenceEquals(session.Document,motionBefore)&&!session.InTransaction&&ReferenceEquals(Canvas.DisplayDocument,motionBefore),"Motion cancellation retained pixels or preview.");
            File.WriteAllText(reportPath,JsonSerializer.Serialize(new{checks=new[]{"slider/latest preview","comparison checkbox","single Apply/Undo/Redo","pixel/mask save roundtrip","close cancellation","new transaction stale result protection","2048px display-only preview","full-resolution Apply and Undo","Motion distance/angle preview","Motion Apply/Undo/Redo/save","Motion close cancellation"},note="Hidden WPF; original Mac pixel comparison deferred."}));
        }
        finally
        {
            dialog?.Close();projectPath=null;session.Load(saved,active);session.EditMask=editMask;Canvas.Fit();Refresh();
            string full=Path.GetFullPath(root),parent=Path.GetFullPath(Path.GetDirectoryName(reportPath)!).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(full.StartsWith(parent,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith("filter-smoke-",StringComparison.Ordinal))Directory.Delete(full,true);
        }
    }
}
