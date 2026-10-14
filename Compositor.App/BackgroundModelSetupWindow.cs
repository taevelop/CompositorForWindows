using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Compositor.Imaging;
namespace Compositor.App;

public sealed class BackgroundModelSetupWindow:Window
{
    private readonly CancellationTokenSource cancellation=new();
    private bool closed;
    public string? InstalledPath {get;private set;}
    internal Button CompactButton {get;}
    internal Button StandardButton {get;}
    internal Button CancelButton {get;}
    internal TextBlock Feedback {get;}
    internal Task Pending {get;private set;}=Task.CompletedTask;
    public BackgroundModelSetupWindow(string directory):this(directory,null){}
    internal BackgroundModelSetupWindow(string directory,Func<bool,CancellationToken,IProgress<long>,Task<string>>? download)
    {
        Title="Background removal setup";Width=450;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Style=(Style)Application.Current.FindResource(typeof(Window));var body=new StackPanel{Margin=new(22)};Content=body;
        body.Children.Add(new TextBlock{Text="Install a background model",FontSize=20,FontWeight=FontWeights.SemiBold});
        body.Children.Add(new TextBlock{Text="Download once, then process images on this PC. Images are not uploaded. Models may miss small subjects; you can edit the resulting layer mask.",TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,16)});
        body.Children.Add(new TextBlock{Text="U²-Net model · download source: rembg GitHub release",Foreground=(Brush)FindResource("MutedInk"),TextWrapping=TextWrapping.Wrap});
        var progress=new TextBlock{Text="Choose a download or import a verified model file.",TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,12)};body.Children.Add(progress);
        var compact=new Button{Content="Download compact model (~5 MB)"};var standard=new Button{Content="Download standard model (~176 MB)"};var import=new Button{Content="Import model file…"};var cancel=new Button{Content="Cancel",IsCancel=true};
        CompactButton=compact;StandardButton=standard;CancelButton=cancel;Feedback=progress;
        foreach(var button in new[]{compact,standard,import,cancel}){button.Style=(Style)FindResource("CompactButton");body.Children.Add(button);}
        async Task Install(bool? small)
        {
            string? source=null;
            if(small is null){var chooser=new Microsoft.Win32.OpenFileDialog{Title="Import verified U2Net model",Filter="ONNX model|*.onnx"};if(chooser.ShowDialog(this)!=true)return;source=chooser.FileName;}
            compact.IsEnabled=standard.IsEnabled=import.IsEnabled=false;progress.Text="Installing model…";
            try
            {
                string path;
                if(small is {} choice){using var client=new System.Net.Http.HttpClient{Timeout=TimeSpan.FromMinutes(10)};var updates=new Progress<long>(bytes=>{if(!closed)progress.Text=$"Downloaded {bytes/1048576d:0.0} MB · verifying before installation";});path=download is null?await BackgroundModelStore.DownloadAsync(directory,client,choice,cancellation.Token,updates):await download(choice,cancellation.Token,updates);}
                else path=await Task.Run(()=>BackgroundModelStore.Install(source!,directory,cancellation.Token),cancellation.Token);
                if(!closed){InstalledPath=path;DialogResult=true;}
            }
            catch(OperationCanceledException) when(cancellation.IsCancellationRequested){}
            catch(Exception error){if(!closed)progress.Text=error.Message;}
            finally{if(!closed)compact.IsEnabled=standard.IsEnabled=import.IsEnabled=true;}
        }
        compact.Click+=(_,_)=>Pending=Install(true);standard.Click+=(_,_)=>Pending=Install(false);import.Click+=(_,_)=>Pending=Install(null);cancel.Click+=(_,_)=>Close();Closed+=(_,_)=>{closed=true;cancellation.Cancel();};
    }
}

