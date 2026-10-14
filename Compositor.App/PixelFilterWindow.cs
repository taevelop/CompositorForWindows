using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;

public enum PixelFilterKind {GaussianBlur,MotionBlur,AddNoise,LensCorrection,ContentAwareFill,RemoveBackground}
public sealed class PixelFilterWindow:Window
{
    private readonly EditorSession session;
    private readonly Document original;
    private readonly Guid layerId;
    private readonly Action<Document?> display;
    private readonly PixelFilterKind kind;
    private readonly BackgroundRemovalJob? backgroundJob;
    private bool background=>kind==PixelFilterKind.RemoveBackground;
    private bool fill=>kind==PixelFilterKind.ContentAwareFill;
    private bool motion=>kind==PixelFilterKind.MotionBlur;
    private bool lens=>kind==PixelFilterKind.LensCorrection;
    private bool noise=>kind==PixelFilterKind.AddNoise;
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
    internal readonly CheckBox GaussianNoise=new(){Content="Gaussian distribution",Margin=new(0,10,0,6)};
    internal readonly CheckBox Monochromatic=new(){Content="Monochromatic",Margin=new(0,4,0,6)};
    internal readonly CheckBox AdvancedMatte=new(){Content="Advanced edge refinement",Margin=new(0,8,0,10)};
    internal readonly Slider RefineMatte=new(){Minimum=0,Maximum=40,Value=12};
    internal readonly Slider ContrastMatte=new(){Minimum=0,Maximum=100,Value=25};
    internal readonly Slider ShiftMatte=new(){Minimum=-10,Maximum=10,Value=0};
    private readonly StackPanel matteControls=new();
    private MatteSettings ReadMatte()=>new(AdvancedMatte.IsChecked==true,RefineMatte.Value,ContrastMatte.Value,ShiftMatte.Value);
    internal uint NoiseSeed {get;}=(uint)Random.Shared.NextInt64(1L<<32);
    internal Task Pending {get;private set;}=Task.CompletedTask;
    public PixelFilterWindow(EditorSession session,Action<Document?> display,PixelFilterKind kind=PixelFilterKind.GaussianBlur,Func<Raster,CancellationToken,LayerMask>? predict=null)
    {
        if(!Enum.IsDefined(kind))throw new ArgumentOutOfRangeException(nameof(kind));
        if(session.InTransaction)throw new InvalidOperationException("Finish the active edit first.");
        if(session.ActiveLayer is not {IsGroup:false,IsAdjustment:false} layer||session.EditMask)
            throw new InvalidOperationException("Select image pixels, not a group, adjustment or mask.");
        if(!LayerHierarchy.Entries(session.Document).First(e=>e.Layer.Id==layer.Id).Visible)throw new InvalidOperationException("Show the image and its parents first.");
        if(kind==PixelFilterKind.ContentAwareFill&&session.Document.Selection is not {IsEmpty:false})throw new InvalidOperationException("Select an area to fill first.");
        if(kind==PixelFilterKind.RemoveBackground&&predict is null)throw new ArgumentException("Background predictor is required.");
        this.session=session;this.display=display;this.kind=kind;original=session.Document;layerId=layer.Id;if(background)backgroundJob=new(original,layerId,predict!);
        if(motion){Radius.Minimum=1;Radius.Maximum=2000;Radius.Value=10;Radius.SmallChange=1;Radius.LargeChange=50;RadiusValue.Text="10";}
        if(noise){Radius.Maximum=400;Radius.Value=10;RadiusValue.Text="10";}
        if(lens){Radius.Minimum=-100;Radius.Maximum=100;Radius.Value=0;RadiusValue.Text="0";}
        Title=background?"Remove Background":fill?"Content-Aware Fill":lens?"Lens Correction":noise?"Add Noise":motion?"Motion Blur":"Gaussian Blur";Width=430;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        Style=(Style)Application.Current.FindResource(typeof(Window));ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var body=new StackPanel{Margin=new(22)};Content=body;
        body.Children.Add(new TextBlock{Text=Title,FontSize=20,FontWeight=FontWeights.SemiBold});
        body.Children.Add(new TextBlock{Text=background?"Hide the background with a layer mask. Original image pixels are preserved.":fill?"Rebuild the selected area using surrounding opaque image pixels.":lens?"Remove distortion · positive: barrel, negative: pincushion":noise?"Amount (%) · changes image colors while preserving alpha":motion?"Distance in layer pixels · positive angles point up-right":"Radius in layer pixels · edges spread into transparency",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("MutedInk"),Margin=new(0,8,0,18)});
        var row=new DockPanel();body.Children.Add(row);DockPanel.SetDock(RadiusValue,Dock.Right);row.Children.Add(RadiusValue);row.Children.Add(new TextBlock{Text=lens?"Remove distortion":noise?"Amount (%)":motion?"Distance":"Radius",VerticalAlignment=VerticalAlignment.Center});
        Radius.Style=(Style)FindResource("EditorSlider");body.Children.Add(Radius);if(fill||background){row.Visibility=Radius.Visibility=reset.Visibility=Visibility.Collapsed;}
        if(motion)
        {
            var angleRow=new DockPanel{Margin=new(0,14,0,0)};body.Children.Add(angleRow);DockPanel.SetDock(AngleValue,Dock.Right);angleRow.Children.Add(AngleValue);angleRow.Children.Add(new TextBlock{Text="Angle (degrees)",VerticalAlignment=VerticalAlignment.Center});
            Angle.Style=(Style)FindResource("EditorSlider");body.Children.Add(Angle);
        }
        if(noise)
        {
            GaussianNoise.Foreground=Monochromatic.Foreground=(Brush)FindResource("Ink");body.Children.Add(GaussianNoise);
            body.Children.Add(new TextBlock{Text="Unchecked: uniform distribution",Foreground=(Brush)FindResource("MutedInk")});body.Children.Add(Monochromatic);
        }
        if(background)
        {
            reset.Visibility=Visibility.Visible;AdvancedMatte.Foreground=(Brush)FindResource("Ink");body.Children.Add(AdvancedMatte);body.Children.Add(matteControls);
            void AddMatteControl(string label,Slider slider)
            {
                var text=new TextBox{Width=70,Text=slider.Value.ToString("0.##",CultureInfo.InvariantCulture)};
                var line=new DockPanel{Margin=new(0,5,0,0)};DockPanel.SetDock(text,Dock.Right);line.Children.Add(text);line.Children.Add(new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center});matteControls.Children.Add(line);
                slider.Style=(Style)FindResource("EditorSlider");matteControls.Children.Add(slider);
                slider.ValueChanged+=(_,_)=>{text.Text=slider.Value.ToString("0.##",CultureInfo.InvariantCulture);if(ready)Queue();};
                void Read(){if(double.TryParse(text.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)&&value>=slider.Minimum&&value<=slider.Maximum)slider.Value=value;else text.Text=slider.Value.ToString("0.##",CultureInfo.InvariantCulture);}
                text.LostKeyboardFocus+=(_,_)=>Read();text.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){Read();e.Handled=true;}};
            }
            AddMatteControl("Refine edges (pixels)",RefineMatte);AddMatteControl("Matte contrast (%)",ContrastMatte);AddMatteControl("Shift edge (pixels)",ShiftMatte);
            matteControls.Visibility=Visibility.Collapsed;
            AdvancedMatte.Checked+=(_,_)=>{matteControls.Visibility=Visibility.Visible;if(ready)Queue();};AdvancedMatte.Unchecked+=(_,_)=>{matteControls.Visibility=Visibility.Collapsed;if(ready)Queue();};
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
            else{RadiusValue.Text=Radius.Value.ToString("0.##",CultureInfo.InvariantCulture);Feedback.Text=lens?"Distortion must be −100–100.":noise?"Amount must be 0.1–400%.":motion?"Distance must be 1–2000 pixels.":"Radius must be 0.1–250 pixels.";}
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
        GaussianNoise.Checked+=(_,_)=>{if(ready)Queue();};GaussianNoise.Unchecked+=(_,_)=>{if(ready)Queue();};Monochromatic.Checked+=(_,_)=>{if(ready)Queue();};Monochromatic.Unchecked+=(_,_)=>{if(ready)Queue();};
        reset.Click+=(_,_)=>{if(background){AdvancedMatte.IsChecked=false;RefineMatte.Value=12;ContrastMatte.Value=25;ShiftMatte.Value=0;}Radius.Value=lens?0:motion||noise?10:1;Angle.Value=0;GaussianNoise.IsChecked=false;Monochromatic.IsChecked=false;};cancel.Click+=(_,_)=>Close();ApplyButton.Click+=async(_,_)=>await ApplyEditAsync();
        Closed+=(_,_)=>{closed=true;cancellation?.Cancel();display(null);if(!finished&&Owns)session.Cancel();};
        session.Begin();generation=session.TransactionGeneration;ready=true;Queue();
    }
    private bool Owns=>session.InTransaction&&session.TransactionGeneration==generation;
    private void ShowPreview(){if(ready&&!closed&&Owns)display(PreviewEnabled.IsChecked==true?prepared:null);}
    private void Queue()
    {
        if(committing)return;
        cancellation?.Cancel();var owner=new CancellationTokenSource();cancellation=owner;
        var settings=ReadSettings();if(!noise&&!lens&&!fill&&!background)margin=Math.Max(margin,settings.Margin);int padding=margin;var noiseSettings=ReadNoise();
        ApplyButton.IsEnabled=false;Feedback.Text="Calculating filter…";Pending=Calculate(owner,settings,noiseSettings,padding,Radius.Value,ReadMatte());
    }
    private BlurSettings ReadSettings()=>new(motion?BlurKind.Motion:BlurKind.Gaussian,Radius.Value,Angle.Value);
    private NoiseSettings ReadNoise()=>new(Radius.Value,GaussianNoise.IsChecked==true?NoiseDistribution.Gaussian:NoiseDistribution.Uniform,Monochromatic.IsChecked==true,NoiseSeed);
    private async Task Calculate(CancellationTokenSource owner,BlurSettings settings,NoiseSettings noiseSettings,int padding,double distortion,MatteSettings matte)
    {
        var token=owner.Token;
        try
        {
            await Task.Delay(40,token);
            var result=await Task.Run(async()=>{await gate.WaitAsync(token);try{return background?backgroundJob!.Prepare(matte,true,token):fill?ContentAwareFill.Apply(original,layerId,token):lens?LensCorrection.Preview(original,layerId,distortion,token):noise?AddNoise.Apply(original,layerId,noiseSettings,token):SpatialBlur.Preview(original,layerId,settings,padding,token);}finally{gate.Release();}},token);
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
        AdvancedMatte.IsEnabled=false;matteControls.IsEnabled=false;GaussianNoise.IsEnabled=false;Monochromatic.IsEnabled=false;
        await Pending;
        if(closed||prepared is null||!Owns){committing=false;if(!closed){Radius.IsEnabled=true;RadiusValue.IsEnabled=true;Angle.IsEnabled=true;AngleValue.IsEnabled=true;reset.IsEnabled=true;AdvancedMatte.IsEnabled=true;matteControls.IsEnabled=true;GaussianNoise.IsEnabled=true;Monochromatic.IsEnabled=true;}return;}
        var owner=new CancellationTokenSource();cancellation=owner;var token=owner.Token;
        var matte=ReadMatte();var settings=ReadSettings();double distortion=Radius.Value;int padding=margin;Feedback.Text="Applying full-size filter…";
        try
        {
            var result=noise||fill?prepared:await Task.Run(async()=>{await gate.WaitAsync(token);try{return background?backgroundJob!.Prepare(matte,false,token):lens?LensCorrection.Apply(original,layerId,distortion,token):SpatialBlur.Apply(original,layerId,settings,padding,token);}finally{gate.Release();}},token);
            if(!closed&&!token.IsCancellationRequested&&Owns){session.Preview(result);session.Commit();finished=true;Close();}
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
        catch(Exception error){if(!closed)Feedback.Text=error.Message;}
        finally
        {
            if(ReferenceEquals(cancellation,owner))cancellation=null;owner.Dispose();committing=false;
            if(!closed){Radius.IsEnabled=true;RadiusValue.IsEnabled=true;Angle.IsEnabled=true;AngleValue.IsEnabled=true;reset.IsEnabled=true;AdvancedMatte.IsEnabled=true;matteControls.IsEnabled=true;GaussianNoise.IsEnabled=true;Monochromatic.IsEnabled=true;ApplyButton.IsEnabled=prepared is not null;}
        }
    }
}





