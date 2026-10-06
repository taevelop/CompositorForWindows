using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Compositor.Core;
namespace Compositor.App;

internal enum HueInputMode { None, Sample, Add, Remove, Target }
public sealed class HueSaturationWindow : Window
{
    private readonly EditorSession session;
    private readonly bool pixelEdit;
    private readonly Document original,editing;
    private readonly Layer layer;
    private HueSaturationAdjustment settings;
    private HueSaturationAdjustment? gestureStart;
    private HueRangeAdjustment? dragStart;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(180)};
    private bool updating,ready,closed,finished;
    internal readonly ComboBox RangePicker=new(){ItemsSource=Enum.GetValues<HueRange>()};
    internal readonly CheckBox Colorize=new(){Content="Colorize",Margin=new(4,8,4,8)};
    internal readonly CheckBox Invert=new(){Content="Apply outside this range",Margin=new(4,8,4,8)};
    internal readonly CheckBox PreviewEnabled=new(){Content="Preview changes",IsChecked=true,Margin=new(4,8,4,8)};
    internal readonly Slider[] Sliders=new Slider[3];
    private readonly List<TextBox> inputs=[];
    private readonly TextBlock feedback=new(){TextWrapping=TextWrapping.Wrap,Margin=new(4),MinHeight=34};
    private readonly TextBlock handles=new(){Margin=new(4)};
    private readonly StackPanel bandPanel=new();
    private readonly Dictionary<HueInputMode,Button> modes=[];
    internal readonly HueSpectrum Spectrum=new();
    internal readonly HuePreview Image;
    internal HueInputMode InputMode { get; private set; }
    internal HueSaturationAdjustment Settings=>settings;

    public HueSaturationWindow(EditorSession session,bool create, bool pixelEdit = false)
    {
        if(session.InTransaction)throw new InvalidOperationException("Finish the active edit first.");
        this.pixelEdit=pixelEdit; this.session=session;original=session.Document;
        if(create)
        {
            var active=session.ActiveLayer;layer=Layer.HueSaturationLayer(original.Width,original.Height,active?.IsGroup==true?active.Id:active?.ParentId);
            int index=active is null?original.Layers.Length:original.Layers.IndexOf(active)+1;
            editing=original with {Layers=original.Layers.Insert(index,layer)};editing.Validate();
        }
        else {layer=session.ActiveLayer??throw new InvalidOperationException("Select Hue/Saturation.");if(layer.HueSaturation is null)throw new InvalidOperationException("Select Hue/Saturation.");editing=original;}
        settings=layer.HueSaturation!.Resolved;
        Title="Hue / Saturation";Width=1040;Height=740;MinWidth=720;MinHeight=420;ShowInTaskbar=false;
        Style=(Style)Application.Current.FindResource(typeof(Window));WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Colorize.Foreground=Invert.Foreground=PreviewEnabled.Foreground=(Brush)FindResource("Ink");
        var root=new Grid{Margin=new(16)};root.ColumnDefinitions.Add(new());root.ColumnDefinitions.Add(new(){Width=new(390)});Content=root;
        Image=new HuePreview{Document=()=>session.Document,Margin=new(0,0,16,0)};
        Image.BeginSample=BeginSample;Image.DragSample=DragSample;Image.EndSample=EndSample;Image.Error=message=>feedback.Text=message;
        var previewArea=new DockPanel();root.Children.Add(previewArea);
        var hint=new TextBlock{Text="Wheel: zoom · Middle drag: pan · Target drag + Ctrl: hue",TextWrapping=TextWrapping.Wrap,Margin=new(4,8,4,0)};
        DockPanel.SetDock(hint,Dock.Bottom);previewArea.Children.Add(hint);
        var fit=new Button{Content="Fit image",Style=(Style)FindResource("CompactButton"),HorizontalAlignment=HorizontalAlignment.Left};
        fit.Click+=(_,_)=>Image.Fit();DockPanel.SetDock(fit,Dock.Top);previewArea.Children.Add(fit);previewArea.Children.Add(Image);
        var panel=new DockPanel();Grid.SetColumn(panel,1);root.Children.Add(panel);
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);panel.Children.Add(footer);
        footer.Children.Add(PreviewEnabled);footer.Children.Add(feedback);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};footer.Children.Add(actions);
        void Button(string text,Action action,bool cancel=false){var b=new Button{Content=text,IsCancel=cancel,Style=(Style)FindResource("CompactButton")};b.Click+=(_,_)=>action();actions.Children.Add(b);}
        Button("Reset",()=>SetSettings(settings.Colorize?HueSaturationAdjustment.ColorizeStart:new()));Button("Cancel",Close,true);Button("Apply",ApplyEdit);
        var body=new StackPanel();panel.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        body.Children.Add(new TextBlock{Text="Color range",Margin=new(4,0,4,4)});body.Children.Add(RangePicker);
        string[] labels=["Hue","Saturation","Lightness"];
        for(int i=0;i<3;i++)
        {
            var line=new DockPanel();line.Children.Add(new TextBlock{Text=labels[i],Margin=new(4,8,4,4)});
            var slider=new Slider{Minimum=i==0?-360:-100,Maximum=i==0?360:100,Style=(Style)FindResource("EditorSlider")};Sliders[i]=slider;
            var input=new TextBox{Width=70,HorizontalAlignment=HorizontalAlignment.Right};
            input.SetBinding(TextBox.TextProperty,new Binding("Value"){Source=slider,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,ValidatesOnExceptions=true,StringFormat="0.##"});
            inputs.Add(input);line.Children.Add(input);body.Children.Add(line);body.Children.Add(slider);
            slider.ValueChanged+=(_,_)=>ReadSliders();input.TextChanged+=(_,_)=>Queue();
        }
        body.Children.Add(Colorize);body.Children.Add(bandPanel);
        bandPanel.Children.Add(new TextBlock{Text="Original hues / range handles / adjusted hues",Margin=new(4,8,4,8),TextWrapping=TextWrapping.Wrap});
        bandPanel.Children.Add(Spectrum);bandPanel.Children.Add(handles);bandPanel.Children.Add(Invert);
        bandPanel.Children.Add(new TextBlock{Text="Drag the four marks. Arrow keys move/select marks.",TextWrapping=TextWrapping.Wrap,Margin=new(4)});
        var tools=new WrapPanel{Margin=new(0,8,0,8)};body.Children.Add(tools);
        foreach(var mode in new[]{HueInputMode.Sample,HueInputMode.Add,HueInputMode.Remove,HueInputMode.Target})
        {
            var b=new Button{Content=mode.ToString(),Style=(Style)FindResource("CompactButton")};modes.Add(mode,b);tools.Children.Add(b);
            b.Click+=(_,_)=>SetMode(InputMode==mode?HueInputMode.None:mode);
        }
        body.Children.Add(new TextBlock{Text="Sample: center the range on an image color. Add/Remove: widen or narrow it. Target: drag a color to change saturation.",TextWrapping=TextWrapping.Wrap,Margin=new(4)});
        RangePicker.SelectionChanged+=(_,_)=>{if(!updating&&RangePicker.SelectedItem is HueRange r){Image.CancelGesture();settings=settings with{Range=r};UpdateControls();Preview();}};
        Colorize.Click+=(_,_)=>SetSettings(Colorize.IsChecked==true?HueSaturationAdjustment.ColorizeStart:new());
        Invert.Click+=(_,_)=>{settings=settings with{InvertRange=Invert.IsChecked==true};Preview();};
        PreviewEnabled.Click+=(_,_)=>Preview();
        Spectrum.BandChanged=band=>{settings=settings with{Bands=settings.Bands.SetItem(settings.Range,band)};UpdateSpectrum();Queue();};
        session.Begin();session.ActiveLayerId=layer.Id;ready=true;timer.Tick+=(_,_)=>Preview();
        Loaded+=(_,_)=>{if(Owner is not null){Height=Math.Min(Height,Math.Max(MinHeight,Owner.ActualHeight));Width=Math.Min(Width,Math.Max(MinWidth,Owner.ActualWidth));}};
        Closed+=(_,_)=>{CancelEdit();Image.Dispose();};Deactivated+=(_,_)=>Image.CancelGesture();
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape&&gestureStart is not null){Image.CancelGesture();e.Handled=true;}};
        UpdateControls();Preview();
    }
    private void UpdateSpectrum(){Spectrum.Settings=settings;Spectrum.InvalidateVisual();handles.Text=string.Join("   ",settings.Band(settings.Range).Handles.Select(x=>$"{x:0.#}°"));}
    private void UpdateControls()
    {
        updating=true;RangePicker.SelectedItem=settings.Range;RangePicker.IsEnabled=!settings.Colorize;
        Colorize.IsChecked=settings.Colorize;Invert.IsChecked=settings.InvertRange;
        var a=settings.Adjustment(settings.Range);
        // Imported legacy values may be outside the normal editing slider range; keep them on no-op edits.
        Sliders[0].Minimum=settings.Colorize?Math.Min(0,a.Hue):Math.Min(-180,a.Hue);
        Sliders[0].Maximum=settings.Colorize?360:Math.Max(180,a.Hue);
        Sliders[1].Minimum=settings.Colorize?Math.Min(0,a.Saturation):-100;
        Sliders[0].Value=a.Hue;Sliders[1].Value=a.Saturation;Sliders[2].Value=a.Lightness;
        bandPanel.Visibility=!settings.Colorize&&settings.Range!=HueRange.Master?Visibility.Visible:Visibility.Collapsed;
        foreach(var (mode,b) in modes)b.IsEnabled=!settings.Colorize&&(mode==HueInputMode.Target||settings.Range!=HueRange.Master);
        if(InputMode!=HueInputMode.None&&!modes[InputMode].IsEnabled)InputMode=HueInputMode.None;
        UpdateModeColors();UpdateSpectrum();updating=false;
    }
    private void ReadSliders()
    {
        if(updating||!ready||closed)return;
        settings=settings with{Adjustments=settings.Adjustments.SetItem(settings.Range,new(Sliders[0].Value,Sliders[1].Value,Sliders[2].Value))};
        UpdateSpectrum();Queue();
    }
    private void Queue(){if(updating||!ready||closed)return;timer.Stop();timer.Start();}
    private Layer EditedLayer()=>settings.Equals(layer.HueSaturation!.Resolved)?layer:layer with{HueSaturation=layer.HueSaturation with{Settings=settings}};
    internal bool Preview()
    {
        timer.Stop();if(!ready||closed)return false;
        if(inputs.Any(Validation.GetHasError)){feedback.Text="Enter valid numbers.";return false;}
        try {
        settings.Validate();session.Preview(PreviewEnabled.IsChecked==true?editing.Replace(EditedLayer()):original);
        Image.InvalidateVisual();feedback.Text=pixelEdit ? "Apply changes image pixels; Undo restores them." : "Source pixels stay unchanged.";return true;
        } catch(Exception error){session.Preview(original);feedback.Text=error.Message;Image.InvalidateVisual();return false;}
    }
    internal void SetSettings(HueSaturationAdjustment value){value.Validate();Image.CancelGesture();settings=value;UpdateControls();if(ready)Preview();}
    internal void SetMode(HueInputMode mode)
    {
        Image.CancelGesture();InputMode=mode==HueInputMode.None||modes[mode].IsEnabled?mode:HueInputMode.None;UpdateModeColors();
    }
    private void UpdateModeColors(){foreach(var (mode,b) in modes)b.Background=(Brush)FindResource(InputMode==mode?"AccentMuted":"Panel");}
    private bool BeginSample(PointD point)
    {
        if(InputMode==HueInputMode.None||settings.Colorize)return false;
        timer.Stop();double? hue=Image.SampleHue(point);if(hue is null){feedback.Text="Choose a visible, non-neutral image color.";return false;}
        gestureStart=settings;dragStart=null;
        if(InputMode==HueInputMode.Target)
        {
            var range=Enum.GetValues<HueRange>().Where(r=>r!=HueRange.Master).MaxBy(r=>settings.Weight(r,hue.Value));
            settings=settings with{Range=range};dragStart=settings.Adjustment(range);
        }
        else
        {
            var band=settings.Band(settings.Range);
            band=InputMode switch{HueInputMode.Sample=>band.Centered(hue.Value),HueInputMode.Add=>band.Include(hue.Value),_=>band.Exclude(hue.Value)};
            settings=settings with{Bands=settings.Bands.SetItem(settings.Range,band)};
        }
        UpdateControls();Preview();return true;
    }
    private void DragSample(double delta,bool control)
    {
        if(dragStart is null)return;
        var current=settings.Adjustment(settings.Range);
        var a=control?current with{Hue=Math.Clamp(dragStart.Hue+delta/2,-180,180)}:current with{Saturation=Math.Clamp(dragStart.Saturation+delta/2,-100,100)};
        settings=settings with{Adjustments=settings.Adjustments.SetItem(settings.Range,a)};UpdateControls();Queue();
    }
    private void EndSample(bool commit)
    {
        if(!commit&&gestureStart is { } before){settings=before;UpdateControls();}
        gestureStart=null;dragStart=null;Preview();
    }
    internal void ApplyEdit()
    {
        if(!Preview())return;try { session.Preview(editing.Replace(EditedLayer()));session.Commit();finished=true;Close(); } catch(Exception error){session.Preview(original);feedback.Text=error.Message;}
    }
    internal void CancelEdit()
    {
        if(closed)return;Image.CancelGesture();closed=true;timer.Stop();if(!finished&&ready)session.Cancel();
    }
}
