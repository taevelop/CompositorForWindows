using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;

public partial class MainWindow
{
    private async Task WandSmoke(string report)
    {
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        var saved=session.Document;var active=session.ActiveLayerId;var tools=CaptureTabTools();
        try
        {
            var bytes=new byte[8*4*4];for(int y=0;y<4;y++)for(int x=0;x<8;x++){int p=(y*8+x)*4;bytes[p]=(byte)(x<4?40:200);bytes[p+1]=30;bytes[p+2]=20;bytes[p+3]=255;}
            var doc=Document.Create(8,4);var layer=doc.Layers[0] with{Pixels=Raster.FromRgba(8,4,bytes)};doc=doc.Replace(layer);session.Load(doc);
            ToolPicker.SelectedIndex=12;WandTolerance.Value=0;WandSamplePoint.IsChecked=true;WandContiguous.IsChecked=true;WandAllLayers.IsChecked=false;Canvas.SelectionMode=Compositor.Core.SelectionMode.Replace;
            Check(Canvas.Tool==EditorTool.MagicWand&&WandOptions.Visibility==Visibility.Visible&&ToolTitle.Text=="Magic Wand","Wand tool/options were not connected.");
            Canvas.BeginPointer(new(1,1),ModifierKeys.None);await subjectPending;
            Check(session.UndoCount==1&&SelectionGeometry.Contains(session.Document.Selection!,new(1,1))&&!SelectionGeometry.Contains(session.Document.Selection!,new(6,1)),"Wand canvas click did not select matching pixels.");
            Canvas.BeginPointer(new(6,1),ModifierKeys.Shift);await subjectPending;Check(SelectionGeometry.Contains(session.Document.Selection!,new(1,1))&&SelectionGeometry.Contains(session.Document.Selection!,new(6,1)),"Wand Shift did not add.");
            Canvas.BeginPointer(new(1,1),ModifierKeys.Alt);await subjectPending;Check(!SelectionGeometry.Contains(session.Document.Selection!,new(1,1))&&SelectionGeometry.Contains(session.Document.Selection!,new(6,1)),"Wand Alt did not subtract.");
            session.Undo();session.Undo();session.Undo();Check(ReferenceEquals(session.Document,doc),"Wand Undo did not restore original.");
            WandSample5.IsChecked=true;WandTolerance.Value=77;WandContiguous.IsChecked=false;WandAllLayers.IsChecked=true;var state=CaptureTabTools();
            WandTolerance.Value=3;WandSamplePoint.IsChecked=true;RestoreTabTools(state);Check(ReadWandSettings()==new WandSettings(77,2,false,true),"Wand per-tab settings did not restore.");
            UpdateLayout();var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(ActualWidth),(int)Math.Ceiling(ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(this);
            var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var output=File.Create(report+".png"))png.Save(output);
            File.WriteAllText(report,JsonSerializer.Serialize(new{checks=new[]{"Wand tool/options","canvas click matching","Shift add/Alt subtract","single edits and Undo source","per-tab settings"},note="WPF BeginPointer/modifier injection. Physical click/key and large native cancellation remain separate."}));
        }
        finally{session.Load(saved,active);RestoreTabTools(tools);Refresh();}
    }
}
