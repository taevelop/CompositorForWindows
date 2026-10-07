using System.Windows;
using System.Windows.Media;
using Compositor.Core;
namespace Compositor.App;
public partial class MainWindow
{
    private void PaletteSmokeTest()
    {
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        var document=session.Document;var tools=CaptureTabTools();
        try
        {
            session.Load(Document.Create(4,4));var original=session.Document;
            BrushColor.Text="#CC3300";SetBackgroundColor("#0033CC");SwapPalette(null,new());
            Check(BrushColor.Text=="#0033CC"&&backgroundColor=="#CC3300","Palette swap lost color.");
            Check(((SolidColorBrush)ColorSwatch.Background).Color.R==0&&((SolidColorBrush)BackgroundColorSwatch.Background).Color.R==204,"Palette swatches did not follow swap.");
            ResetPalette(null,new());Check(BrushColor.Text=="#000000"&&backgroundColor=="#FFFFFF","Default colors incorrect.");
            Check(ReferenceEquals(original,session.Document)&&!session.IsModified&&session.UndoCount==0,"Palette editing modified document history.");
            var layer=original.Layers[0] with{Mask=LayerMask.Solid(4,4)};session.Load(original.Replace(layer),layer.Id);session.EditMask=true;Refresh();
            MaskSlider.Value=25;SwapPalette(null,new());Check(MaskSlider.Value==75&&BrushColor.Text=="#000000"&&backgroundColor=="#FFFFFF","Mask swap changed image palette.");
            Check(((SolidColorBrush)ColorSwatch.Background).Color.R==191,"Mask palette did not show current gray.");
            ResetPalette(null,new());Check(MaskSlider.Value==0&&session.UndoCount==0,"Mask default did not reset without editing history.");
        }
        finally{session.Load(document);RestoreTabTools(tools);Refresh();}
    }
}
