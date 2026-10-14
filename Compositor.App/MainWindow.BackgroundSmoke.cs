using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;

public partial class MainWindow
{
    internal async Task BackgroundModelSmoke(string report,string model,string imagePath)
    {
        void Check(bool valid,string message){if(!valid)throw new InvalidOperationException(message);}
        var source=ImageCodec.Load(imagePath);var original=Document.Create(source.Width,source.Height);
        var layer=original.Layers[0] with{Pixels=source};original=original.Replace(layer);session.Load(original);Canvas.Fit();
        int calls=0;LayerMask? inferred=null;
        LayerMask Predict(Raster pixels,CancellationToken token){calls++;using var predictor=new U2NetPredictor(model);return inferred=predictor.Predict(pixels,token);}
        PixelFilterWindow? dialog=null;string folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"background-model-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        long allocated=GC.GetTotalAllocatedBytes(true);var clock=Stopwatch.StartNew();
        try
        {
            dialog=new(session,Canvas.ShowFilterPreview,PixelFilterKind.RemoveBackground,Predict){Owner=this};dialog.Show();await dialog.Pending;
            Check(dialog.ApplyButton.IsEnabled&&calls==1&&inferred is not null,"Actual model preview failed.");
            Check(ReferenceEquals(session.Document,original),"Inference changed the source document.");
            dialog.AdvancedMatte.IsChecked=true;dialog.RefineMatte.Value=12;dialog.ContrastMatte.Value=25;await dialog.Pending;
            Check(dialog.ApplyButton.IsEnabled&&calls==1,"Advanced refinement repeated model inference.");
            var expected=await Task.Run(()=>SubjectMask.Apply(original,layer.Id,MatteRefinement.Apply(inferred!,source,new(true,12,25,0))));
            dialog.PreviewEnabled.IsChecked=false;Check(ReferenceEquals(Canvas.DisplayDocument,original),"Background comparison failed.");dialog.PreviewEnabled.IsChecked=true;
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth),(int)Math.Ceiling(dialog.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(dialog);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(report+".panel.png"))encoder.Save(output);
            await dialog.ApplyEditAsync();dialog=null;
            Check(calls==1&&session.UndoCount==1&&ReferenceEquals(session.ActiveLayer!.Pixels,source),"Apply lost source or repeated inference.");
            Check(CompositePixels(expected).SequenceEqual(CompositePixels(session.Document)),"Apply used preview refinement instead of full-resolution refinement.");
            var result=session.Document;Undo(this,new());Check(ReferenceEquals(session.Document,original),"Actual model Undo lost source.");Redo(this,new());
            var pixels=CompositePixels(result);projectPath=Path.Combine(folder,"Background.comp");Check(await Save(false)&&await ReopenSavedProject(projectPath)&&pixels.SequenceEqual(CompositePixels(session.Document)),"Actual model project roundtrip failed.");
            ImageCodec.Export(session.Document,report+".result.jpg",true);
            File.WriteAllText(report,JsonSerializer.Serialize(new{calls,width=source.Width,height=source.Height,totalMs=clock.Elapsed.TotalMilliseconds,allocatedMiB=(GC.GetTotalAllocatedBytes(true)-allocated)/1048576d,workingSetMiB=Process.GetCurrentProcess().WorkingSet64/1048576d,checks=new[]{"actual ONNX WPF preview","Advanced cached inference","comparison/source immutable","full-resolution Apply reference","single Undo/Redo","project mask/output roundtrip"},note="Development PC actual hidden WPF. End-to-end elapsed and cumulative managed allocations, not peak memory or quality ground truth."},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally
        {
            dialog?.Close();projectPath=null;
            string parent=Path.GetDirectoryName(Path.GetFullPath(report))!+Path.DirectorySeparatorChar;
            if(Path.GetFullPath(folder).StartsWith(parent,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(folder).StartsWith("background-model-",StringComparison.Ordinal))Directory.Delete(folder,true);
        }
    }
}
