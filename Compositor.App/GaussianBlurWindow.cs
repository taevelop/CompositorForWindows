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
    private readonly Action<Document?> display;
    private readonly bool motion;
    private readonly long generation;
    private readonly SemaphoreSlim gate=new(1,1);
    private CancellationTokenSource? cancellation;
    private Document? prepared;
    private bool closed,finished,ready,committing;
    private int margin;
    internal readonly Slider Radius=new(){Minimum=.1,Maximum=250,Value=1,SmallChange=.1,LargeChange=5};
    internal readonly TextBox RadiusValue=new(){Text="1",Width=70};
    internal readonly CheckBox PreviewEnabled=new(){Content="Preview changes",IsChecked=true,Margin=new(0,14,0,10)};
    internal readonly TextBlock Feedback=new(){MinHeight=38,TextWrapping=TextWrapping.Wrap};
    internal readonly Button ApplyButton=new(){Content="Apply",IsEnabled=false};
    private readonly Button reset=new(){Content="Reset"};
    internal readonly Slider Angle=new(){Minimum=-90,Maximum=90,Value=0,SmallChange=1,LargeChange=15};
    internal readonly TextBox AngleValue=new(){Text="0",Width=70};
    internal Task Pending {get;private set;}=Task.CompletedTask;
    public GaussianBlurWindow(EditorSession session,Action<Document?> display,bool motion=false)
    {
        if(session.InTransaction)throw new InvalidOperationException("Finish the active edit first.");
        if(session.ActiveLayer is not {IsGroup:false,IsAdjustment:false} layer||session.EditMask)
            throw new InvalidOperationException("Select image pixels, not a group, adjustment or mask.");
        if(!LayerHierarchy.Entries(session.Document).First(e=>e.Layer.Id==layer.Id).Visible)throw new InvalidOperationException("Show the image and its parents first.");
        this.session=session;this.display=display;this.motion=motion;original=session.Document;layerId=layer.Id;
        if(motion){Radius.Minimum=1;Radius.Maximum=2000;Radius.Value=10;Radius.SmallChange=1;Radius.LargeChange=50;RadiusValue.Text="10";}
        Title=motion?"Motion Blur":"Gaussian Blur";Width=430;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        Style=(Style)Application.Current.FindResource(typeof(Window));ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var body=new StackPanel{Margin=new(22)};Content=body;
        body.Children.Add(new TextBlock{Text=Title,FontSize=20,FontWeight=FontWeights.SemiBold});
        body.Children.Add(new TextBlock{Text=motion?"Distance in layer pixels · positive angles point up-right":"Radius in layer pixels · edges spread into transparency",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("MutedInk"),Margin=new(0,8,0,18)});
        var row=new DockPanel();body.Children.Add(row);DockPanel.SetDock(RadiusValue,Dock.Right);row.Children.Add(RadiusValue);row.Children.Add(new TextBlock{Text=motion?"Distance":"Radius",VerticalAlignment=VerticalAlignment.Center});
        Radius.Style=(Style)FindResource("EditorSlider");body.Children.Add(Radius);
        if(motion)
        {
            var angleRow=new DockPanel{Margin=new(0,14,0,0)};body.Children.Add(angleRow);DockPanel.SetDock(AngleValue,Dock.Right);angleRow.Children.Add(AngleValue);angleRow.Children.Add(new TextBlock{Text="Angle (degrees)",VerticalAlignment=VerticalAlignment.Center});
            Angle.Style=(Style)FindResource("EditorSlider");body.Children.Add(Angle);
        }
        body.Children.Add(PreviewEnabled);body.Children.Add(Feedback);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};body.Children.Add(buttons);
        var cancel=new Button{Content="Cancel",IsCancel=true};
        foreach(var button in new[]{reset,cancel,ApplyButton}){button.Style=(Style)FindResource("CompactButton");buttons.Children.Add(button);}
        PreviewEnabled.Foreground=(Brush)FindResource("Ink");
        Radius.ValueChanged+=(_,_)=>{RadiusValue.Text=Radius.Value.ToString("0.##",CultureInfo.InvariantCulture);if(ready)Queue();};
        void ReadValue()
        {
            if(double.TryParse(RadiusValue.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double value)&&double.IsFinite(value)&&value>=Radius.Minimum&&value<=Radius.Maximum)
            {if(Radius.Value!=value)Radius.Value=value;}
            else{RadiusValue.Text=Radius.Value.ToString("0.##",CultureInfo.InvariantCulture);Feedback.Text=motion?"Distance must be 1–2000 pixels.":"Radius must be 0.1–250 pixels.";}
        }
        RadiusValue.LostKeyboardFocus+=(_,_)=>ReadValue();RadiusValue.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){ReadValue();e.Handled=true;}};
        Angle.ValueChanged+=(_,_)=>{AngleValue.Text=Angle.Value.ToString("0.##",CultureInfo.InvariantCulture);if(ready)Queue();};
        void ReadAngle()
        {
            if(double.TryParse(AngleValue.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double value)&&double.IsFinite(value)&&value>= -90&&value<=90)Angle.Value=value;
            else{AngleValue.Text=Angle.Value.ToString("0.##",CultureInfo.InvariantCulture);Feedback.Text="Angle must be −90–90 degrees.";}
        }
        AngleValue.LostKeyboardFocus+=(_,_)=>ReadAngle();AngleValue.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){ReadAngle();e.Handled=true;}};
        PreviewEnabled.Checked+=(_,_)=>ShowPreview();PreviewEnabled.Unchecked+=(_,_)=>ShowPreview();
        reset.Click+=(_,_)=>{Radius.Value=motion?10:1;Angle.Value=0;};cancel.Click+=(_,_)=>Close();ApplyButton.Click+=async(_,_)=>await ApplyEditAsync();
        Closed+=(_,_)=>{closed=true;cancellation?.Cancel();display(null);if(!finished&&Owns)session.Cancel();};
        session.Begin();generation=session.TransactionGeneration;ready=true;Queue();
    }
    private bool Owns=>session.InTransaction&&session.TransactionGeneration==generation;
    private void ShowPreview(){if(ready&&!closed&&Owns)display(PreviewEnabled.IsChecked==true?prepared:null);}
    private void Queue()
    {
        if(committing)return;
        cancellation?.Cancel();var owner=new CancellationTokenSource();cancellation=owner;
        var settings=ReadSettings();margin=Math.Max(margin,settings.Margin);int padding=margin;
        ApplyButton.IsEnabled=false;Feedback.Text="Calculating blur…";Pending=Calculate(owner,settings,padding);
    }
    private BlurSettings ReadSettings()=>new(motion?BlurKind.Motion:BlurKind.Gaussian,Radius.Value,Angle.Value);
    private async Task Calculate(CancellationTokenSource owner,BlurSettings settings,int padding)
    {
        var token=owner.Token;
        try
        {
            await Task.Delay(40,token);
            var result=await Task.Run(async()=>{await gate.WaitAsync(token);try{return SpatialBlur.Preview(original,layerId,settings,padding,token);}finally{gate.Release();}},token);
            if(closed||token.IsCancellationRequested||!ReferenceEquals(cancellation,owner)||!Owns)return;
            prepared=result;ShowPreview();ApplyButton.IsEnabled=true;Feedback.Text="Ready · one Undo when applied";
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
        catch(Exception error){if(!closed&&ReferenceEquals(cancellation,owner)){prepared=null;Feedback.Text=error.Message;ApplyButton.IsEnabled=false;}}
        finally{if(ReferenceEquals(cancellation,owner))cancellation=null;owner.Dispose();}
    }
    internal async Task ApplyEditAsync()
    {
        if(committing||closed)return;committing=true;
        Radius.IsEnabled=false;RadiusValue.IsEnabled=false;Angle.IsEnabled=false;AngleValue.IsEnabled=false;reset.IsEnabled=false;ApplyButton.IsEnabled=false;
        await Pending;
        if(closed||prepared is null||!Owns){committing=false;if(!closed){Radius.IsEnabled=true;RadiusValue.IsEnabled=true;Angle.IsEnabled=true;AngleValue.IsEnabled=true;reset.IsEnabled=true;}return;}
        var owner=new CancellationTokenSource();cancellation=owner;var token=owner.Token;
        var settings=ReadSettings();int padding=margin;Feedback.Text="Applying full-size blur…";
        try
        {
            var result=await Task.Run(async()=>{await gate.WaitAsync(token);try{return SpatialBlur.Apply(original,layerId,settings,padding,token);}finally{gate.Release();}},token);
            if(!closed&&!token.IsCancellationRequested&&Owns){session.Preview(result);session.Commit();finished=true;Close();}
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
        catch(Exception error){if(!closed)Feedback.Text=error.Message;}
        finally
        {
            if(ReferenceEquals(cancellation,owner))cancellation=null;owner.Dispose();committing=false;
            if(!closed){Radius.IsEnabled=true;RadiusValue.IsEnabled=true;Angle.IsEnabled=true;AngleValue.IsEnabled=true;reset.IsEnabled=true;ApplyButton.IsEnabled=prepared is not null;}
        }
    }
}
