using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
namespace Compositor.App;
public partial class MainWindow
{
    private void PickerSamplingSmokeTest()
    {
        var ring=new PickerSampleRing(Color.FromRgb(18,52,86));
        ring.Measure(new Size(220,220));ring.Arrange(new Rect(0,0,220,220));
        byte[] RenderRing(bool visible)
        {
            ring.Update(new Point(100,100),Colors.Lime,visible);ring.UpdateLayout();
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(220,220,96,96,PixelFormats.Pbgra32);
            bitmap.Render(ring);var pixels=new byte[220*220*4];bitmap.CopyPixels(pixels,220*4,0);return pixels;
        }
        var pixels=RenderRing(true);
        int top=(57*220+100)*4,bottom=(143*220+100)*4;
        if(pixels[top+1]!=255||pixels[top+3]!=255||pixels[bottom]!=86||pixels[bottom+1]!=52||pixels[bottom+2]!=18)
            throw new InvalidOperationException("Picker comparison ring colors differ.");
        if(RenderRing(false).Any(value=>value!=0))throw new InvalidOperationException("Disabled picker comparison ring remains visible.");
        var document=session.Document;var foreground=BrushColor.Text;
        Canvas.RestoreView(2,100,100);
        foreach(bool accept in new[]{false,true})
        {
            var picker=new ColorPickerWindow(Color.FromRgb(18,52,86)){Owner=this};Exception? failure=null;
            picker.Loaded+=(_,_)=>picker.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
            {
                try
                {
                    picker.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
                    {
                        try
                        {
                            var surface=picker.OwnedWindows.OfType<CanvasColorSampleWindow>().Single();
                            surface.Sample(new Point(102,100));
                            if(surface.Sampled!=Colors.Lime||picker.SelectedColor!=Colors.Lime)throw new InvalidOperationException("Picker sampling ignored document coordinates.");
                            surface.Sample(new Point(104,100));
                            if(surface.Sampled!=Colors.Lime||picker.SelectedColor!=Colors.Lime)throw new InvalidOperationException("Transparent picker sample discarded prior color.");
                            surface.DialogResult=accept;
                        }
                        catch(Exception ex){failure=ex;foreach(Window child in picker.OwnedWindows)child.Close();}
                    }));
                    bool result=SamplePickerColor(picker);
                    if(result!=accept||picker.SelectedColor!=(accept?Colors.Lime:Color.FromRgb(18,52,86)))throw new InvalidOperationException("Picker sampling accept/cancel changed wrong color.");
                    if(BrushColor.Text!=foreground||!ReferenceEquals(document,session.Document)||session.UndoCount!=0)throw new InvalidOperationException("Picker sampling mutated palette or document history.");
                }
                catch(Exception ex){failure=ex;}
                finally{picker.DialogResult=false;}
            }));
            picker.ShowDialog();if(failure is not null)throw failure;
        }
    }
}
