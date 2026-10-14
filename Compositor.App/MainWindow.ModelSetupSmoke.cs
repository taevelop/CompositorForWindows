using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Compositor.App;

public partial class MainWindow
{
    private async Task ModelSetupSmoke(string report)
    {
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        async Task Run(Func<bool,CancellationToken,IProgress<long>,Task<string>> downloader,Func<BackgroundModelSetupWindow,Task> inspect)
        {
            var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var setup=new BackgroundModelSetupWindow(Path.GetDirectoryName(Path.GetFullPath(report))!,downloader){Owner=this};
            setup.Loaded+=async(_,_)=>
            {
                try{await inspect(setup);done.TrySetResult();}
                catch(Exception error){done.TrySetException(error);}
                finally{if(setup.IsVisible)setup.Close();}
            };
            setup.ShowDialog();await done.Task;
        }
        var release=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run(async(compact,token,progress)=>{Check(compact,"Compact button selected wrong model.");progress.Report(1048576);return await release.Task.WaitAsync(token);},async setup=>
        {
            UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(setup.ActualWidth),(int)Math.Ceiling(setup.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(setup);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(report+".png"))encoder.Save(output);
            setup.CompactButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await System.Windows.Threading.Dispatcher.Yield();Check(!setup.CompactButton.IsEnabled&&!setup.StandardButton.IsEnabled&&setup.Feedback.Text.Contains("1.0 MB"),"Download progress or input lock failed.");
            release.SetResult("verified-model.onnx");await setup.Pending;Check(setup.InstalledPath=="verified-model.onnx"&&!setup.IsVisible,"Completed download did not close with installed path.");
        });
        bool observedCancellation=false;
        await Run(async(compact,token,progress)=>{Check(!compact,"Standard button selected wrong model.");try{await Task.Delay(Timeout.Infinite,token);return "unexpected";}catch(OperationCanceledException){observedCancellation=true;throw;}},async setup=>
        {
            setup.StandardButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var pending=setup.Pending;setup.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await pending;
            Check(observedCancellation&&setup.InstalledPath is null,"Cancel failed to stop active download.");
        });
        await Run((compact,token,progress)=>Task.FromException<string>(new InvalidDataException("checksum test failure")),async setup=>
        {
            setup.CompactButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await setup.Pending;
            Check(setup.IsVisible&&setup.CompactButton.IsEnabled&&setup.Feedback.Text=="checksum test failure"&&setup.InstalledPath is null,"Failed download did not restore retry UI.");
        });
        var late=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run((compact,token,progress)=>late.Task,async setup=>
        {
            setup.CompactButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var pending=setup.Pending;setup.Close();late.SetResult("late.onnx");await pending;
            Check(setup.InstalledPath is null,"Late completion after close reported installation success.");
        });
        File.WriteAllText(report,JsonSerializer.Serialize(new{checks=new[]{"modal setup layout","routed compact/standard model buttons","progress and input locking","success close","active download cancellation","failure retry","late completion discarded"},note="Injected downloader WPF tests; actual network install tested separately. Not physical mouse/file-picker verification."}));
    }
}
