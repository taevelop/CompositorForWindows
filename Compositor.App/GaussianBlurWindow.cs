using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;

public sealed class GaussianBlurWindow:Window
{
    private readonly EditorSession session;
    private readonly Document original;
    private readonly Guid layerId;
    private readonly long generation;
    private readonly SemaphoreSlim gate=new(1,1);
    private CancellationTokenSource? cancellation;
    private Document? prepared;
    private bool closed,finished,ready;
    private int margin;
    internal readonly Slider Radius=new(){Minimum=.1,Maximum=250,Value=1,SmallChange=.1,LargeChange=5};
    internal readonly TextBox RadiusValue=new(){Text="1",Width=70};
    internal readonly CheckBox PreviewEnabled=new(){Content="Preview changes",IsChecked=true,Margin=new(0,14,0,10)};
    internal readonly TextBlock Feedback=new(){MinHeight=38,TextWrapping=TextWrapping.Wrap};
    internal readonly Button ApplyButton=new(){Content="Apply",IsEnabled=false};
    internal Task Pending {get;private set;}=Task.CompletedTask;
    public GaussianBlurWindow(EditorSession session)
    {
        if(session.InTransaction)throw new InvalidOperationException("Finish the active edit first.");
        if(session.ActiveLayer is not {IsGroup:false,IsAdjustment:false} layer||session.EditMask)
            throw new InvalidOperationException("Select image pixels, not a group, adjustment or mask.");
        if(!LayerHierarchy.Entries(session.Document).First(e=>e.Layer.Id==layer.Id).Visible)throw new InvalidOperationException("Show the image and its parents first.");
        this.session=session;original=session.Document;layerId=layer.Id;
        Title="Gaussian Blur";Width=430;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        Style=(Style)Application.Current.FindResource(typeof(Window));ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var body=new StackPanel{Margin=new(22)};Content=body;
        body.Children.Add(new TextBlock{Text="Gaussian Blur",FontSize=20,FontWeight=FontWeights.SemiBold});
        body.Children.Add(new TextBlock{Text="Radius in layer pixels · edges spread into transparency",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("MutedInk"),Margin=new(0,8,0,18)});
        var row=new DockPanel();body.Children.Add(row);DockPanel.SetDock(RadiusValue,Dock.Right);row.Children.Add(RadiusValue);row.Children.Add(new TextBlock{Text="Radius",VerticalAlignment=VerticalAlignment.Center});
        Radius.Style=(Style)FindResource("EditorSlider");body.Children.Add(Radius);body.Children.Add(PreviewEnabled);body.Children.Add(Feedback);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};body.Children.Add(buttons);
        var reset=new Button{Content="Reset"};var cancel=new Button{Content="Cancel",IsCancel=true};
        foreach(var button in new[]{reset,cancel,ApplyButton}){button.Style=(Style)FindResource("CompactButton");buttons.Children.Add(button);}
        PreviewEnabled.Foreground=(Brush)FindResource("Ink");
        Radius.ValueChanged+=(_,_)=>{RadiusValue.Text=Radius.Value.ToString("0.##",CultureInfo.InvariantCulture);if(ready)Queue();};
        void ReadValue()
        {
            if(double.TryParse(RadiusValue.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double value)&&double.IsFinite(value)&&value>=.1&&value<=250)
            {if(Radius.Value!=value)Radius.Value=value;}
            else{RadiusValue.Text=Radius.Value.ToString("0.##",CultureInfo.InvariantCulture);Feedback.Text="Radius must be 0.1–250 pixels.";}
        }
        RadiusValue.LostKeyboardFocus+=(_,_)=>ReadValue();RadiusValue.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){ReadValue();e.Handled=true;}};
        PreviewEnabled.Checked+=(_,_)=>ShowPreview();PreviewEnabled.Unchecked+=(_,_)=>ShowPreview();
        reset.Click+=(_,_)=>Radius.Value=1;cancel.Click+=(_,_)=>Close();ApplyButton.Click+=async(_,_)=>await ApplyEditAsync();
        Closed+=(_,_)=>{closed=true;cancellation?.Cancel();if(!finished&&Owns)session.Cancel();};
        session.Begin();generation=session.TransactionGeneration;ready=true;Queue();
    }
    private bool Owns=>session.InTransaction&&session.TransactionGeneration==generation;
    private void ShowPreview(){if(ready&&!closed&&Owns)session.Preview(PreviewEnabled.IsChecked==true&&prepared is not null?prepared:original);}
    private void Queue()
    {
        cancellation?.Cancel();var owner=new CancellationTokenSource();cancellation=owner;
        double radius=Radius.Value;margin=Math.Max(margin,(int)Math.Ceiling(radius*3+2));int padding=margin;
        ApplyButton.IsEnabled=false;Feedback.Text="Calculating blur…";Pending=Calculate(owner,radius,padding);
    }
    private async Task Calculate(CancellationTokenSource owner,double radius,int padding)
    {
        var token=owner.Token;
        try
        {
            await Task.Delay(40,token);
            var result=await Task.Run(async()=>{await gate.WaitAsync(token);try{return GaussianBlur.Apply(original,layerId,radius,padding,token);}finally{gate.Release();}},token);
            if(closed||token.IsCancellationRequested||!ReferenceEquals(cancellation,owner)||!Owns)return;
            prepared=result;ShowPreview();ApplyButton.IsEnabled=true;Feedback.Text="Ready · one Undo when applied";
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
        catch(Exception error){if(!closed&&ReferenceEquals(cancellation,owner)){prepared=null;Feedback.Text=error.Message;ApplyButton.IsEnabled=false;}}
        finally{if(ReferenceEquals(cancellation,owner))cancellation=null;owner.Dispose();}
    }
    internal async Task ApplyEditAsync()
    {
        Radius.IsEnabled=false;RadiusValue.IsEnabled=false;ApplyButton.IsEnabled=false;
        await Pending;
        if(!closed&&prepared is not null&&Owns){session.Preview(prepared);session.Commit();finished=true;Close();}
        else if(!closed){Radius.IsEnabled=true;RadiusValue.IsEnabled=true;}
    }
}
