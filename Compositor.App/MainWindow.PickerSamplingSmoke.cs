using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
namespace Compositor.App;
public partial class MainWindow
{
    private void PickerSamplingSmokeTest()
    {
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
                            if(surface.Sampled!=Colors.Lime)throw new InvalidOperationException("Picker sampling ignored document coordinates.");
                            surface.Sample(new Point(104,100));
                            if(surface.Sampled!=Colors.Lime)throw new InvalidOperationException("Transparent picker sample discarded prior color.");
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
