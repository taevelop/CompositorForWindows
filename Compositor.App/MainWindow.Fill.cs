using System.Windows;
using System.Windows.Media;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    private async void FillForeground(object sender,RoutedEventArgs e)=>await FillPalette(false);
    private async void FillBackground(object sender,RoutedEventArgs e)=>await FillPalette(true);
    private async Task FillPalette(bool background)
    {
        if(busy)return;if(Canvas.IsTransforming)Canvas.CommitTransform();
        if(session.InTransaction||session.ActiveLayer is not {} layer)return;
        var target=session;var original=target.Document;bool mask=target.EditMask;
        Color color;
        if(mask){byte gray=(byte)Math.Round(Math.Clamp(MaskSlider.Value,0,100)*255/100);if(background)gray=(byte)(255-gray);color=Color.FromRgb(gray,gray,gray);}
        else if(!ColorPickerWindow.TryHex(background?backgroundColor:BrushColor.Text,out color))return;
        Document? filled=null;
        if(await Work(mask?"Filling mask…":"Filling pixels…",()=>filled=LayerFill.Apply(original,layer.Id,color.R,color.G,color.B,mask)))
        {
            if(ReferenceEquals(session,target)&&ReferenceEquals(target.Document,original)&&filled is not null)target.Apply(_=>filled);
            Refresh();Canvas.Focus();
        }
    }
    private async Task FillSmokeTest()
    {
        var original=session.Document;var tools=CaptureTabTools();
        try
        {
            var doc=Document.Create(3,1);session.Load(doc);BrushColor.Text="#123456";SetBackgroundColor("#ABCDEF");
            await FillPalette(false);
            if(!session.Document.Layers[0].Pixels.ToRgba()[..4].SequenceEqual(new byte[]{18,52,86,255})||session.UndoCount!=1)throw new InvalidOperationException("Foreground fill failed.");
            await FillPalette(true);
            if(!session.Document.Layers[0].Pixels.ToRgba()[..4].SequenceEqual(new byte[]{171,205,239,255}))throw new InvalidOperationException("Background fill failed.");
            session.Undo();session.Undo();if(!ReferenceEquals(doc,session.Document))throw new InvalidOperationException("Fill Undo failed.");
            doc=doc.Replace(doc.Layers[0] with{Mask=LayerMask.Solid(1,1)});session.Load(doc);session.EditMask=true;MaskSlider.Value=25;
            await FillPalette(true);
            if(session.Document.Layers[0].Mask!.Pixels.ToRgba()[0]!=191)throw new InvalidOperationException("Mask background fill failed.");
        }
        finally{session.Load(original);RestoreTabTools(tools);Refresh();}
    }
}
