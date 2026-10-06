using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class CanvasSizeWindow:Window
{
    private readonly Document original;
    private readonly CanvasSizeDraft draft;
    private readonly Color foreground;
    private Color custom=Colors.White;
    private readonly CancellationTokenSource cancellation=new();
    private bool updating,working;
    private int anchor=4;
    public Document? ResultDocument {get;private set;}
    internal Task PendingApply {get;private set;}=Task.CompletedTask;
    public CanvasSizeWindow(Document document,Color foregroundColor)
    {
        original=document;foreground=foregroundColor;
        draft=new(document.Width,document.Height,document.Resolution);
        InitializeComponent();
        CurrentLabel.Text=$"Current: {document.Width:N0} × {document.Height:N0} pixels";
        updating=true;UnitPicker.ItemsSource=Enum.GetValues<CanvasUnit>();UnitPicker.SelectedIndex=0;
        FillPicker.ItemsSource=new[]{"Transparent","Foreground","Black","White","Custom"};FillPicker.SelectedIndex=0;
        string[] names=["Top left","Top center","Top right","Middle left","Center","Middle right","Bottom left","Bottom center","Bottom right"];
        for(int i=0;i<9;i++)
        {
            var button=new ToggleButton{Content="○",Tag=i,ToolTip=names[i],Width=27,Height=27,Margin=new Thickness(1)};
            System.Windows.Automation.AutomationProperties.SetName(button,names[i]);
            button.Click+=(_,_)=>{anchor=(int)button.Tag;RefreshPreview();};
            Anchors.Children.Add(button);
        }
        updating=false;SyncFields();RefreshPreview();
        Closed+=(_,_)=>cancellation.Cancel();
    }
    private void SyncFields()
    {
        updating=true;WidthField.Text=draft.Displayed(true).ToString("0.###",CultureInfo.InvariantCulture);
        HeightField.Text=draft.Displayed(false).ToString("0.###",CultureInfo.InvariantCulture);updating=false;
    }
    private void ChangeDimension(bool width)
    {
        if(updating)return;
        try
        {
            if(!double.TryParse((width?WidthField:HeightField).Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double n))
                throw new InvalidOperationException("Enter a valid dimension.");
            draft.Set(n,width);
            if(draft.Locked)
            {
                updating=true;(width?HeightField:WidthField).Text=draft.Displayed(!width).ToString("0.###",CultureInfo.InvariantCulture);updating=false;
            }
            RefreshPreview();
        }
        catch(Exception e){ErrorLabel.Text=e.Message;ApplyButton.IsEnabled=false;}
    }
    private void WidthChanged(object sender,TextChangedEventArgs e)=>ChangeDimension(true);
    private void HeightChanged(object sender,TextChangedEventArgs e)=>ChangeDimension(false);
    private void UnitChanged(object sender,SelectionChangedEventArgs e)
    {if(updating||UnitPicker.SelectedItem is not CanvasUnit unit)return;draft.Unit=unit;SyncFields();RefreshPreview();}
    private void RelativeChanged(object sender,RoutedEventArgs e){draft.Relative=Relative.IsChecked==true;SyncFields();RefreshPreview();}
    private void LockedChanged(object sender,RoutedEventArgs e){draft.Locked=Locked.IsChecked==true;if(draft.Locked)draft.Set(draft.Displayed(true),true);SyncFields();RefreshPreview();}
    private Color? FillColor => (FillPicker.SelectedItem as string) switch
    {"Foreground"=>foreground,"Black"=>Colors.Black,"White"=>Colors.White,"Custom"=>custom,_=>null};
    internal CanvasSizeOptions ReadOptions()
    {
        var size=draft.Dimensions();var color=FillColor;
        return new(size.Width,size.Height,anchor,color is {} c?new CanvasFill(c.R,c.G,c.B):null);
    }
    private void FillChanged(object sender,SelectionChangedEventArgs e){if(!updating)RefreshPreview();}
    private void PickColor(object sender,RoutedEventArgs e)
    {
        var dialog=new ColorPickerWindow(custom){Owner=this};
        if(dialog.ShowDialog()==true){custom=dialog.SelectedColor;FillPicker.SelectedItem="Custom";RefreshPreview();}
    }
    private void RefreshPreview()
    {
        if(updating)return;
        ColorButton.IsEnabled=FillPicker.SelectedItem as string=="Custom";
        ColorSwatch.Background=new SolidColorBrush(custom);
        foreach(ToggleButton b in Anchors.Children){b.IsChecked=(int)b.Tag==anchor;b.Content=b.IsChecked==true?"●":"○";}
        try
        {
            // Reject an unfinished field even when the other controls change.
            if(!double.TryParse(WidthField.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var w)||!double.IsFinite(w)||
               !double.TryParse(HeightField.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var h)||!double.IsFinite(h))
                throw new InvalidOperationException("Enter valid width and height values.");
            var options=ReadOptions();var offset=options.Offset(original.Width,original.Height);
            double minX=Math.Min(0,offset.X),minY=Math.Min(0,offset.Y);
            double maxX=Math.Max(options.Width,offset.X+original.Width),maxY=Math.Max(options.Height,offset.Y+original.Height);
            double scale=Math.Min(310/(maxX-minX),115/(maxY-minY));
            Preview.Children.Clear();
            void Box(double x,double y,double width,double height,Brush stroke,Brush fill)
            {
                var box=new Rectangle{Width=width*scale,Height=height*scale,Stroke=stroke,StrokeThickness=1,Fill=fill};
                Canvas.SetLeft(box,15+(x-minX)*scale);Canvas.SetTop(box,12+(y-minY)*scale);Preview.Children.Add(box);
            }
            var blue=new SolidColorBrush(Color.FromRgb(108,154,224));
            Box(0,0,options.Width,options.Height,blue,FillColor is {} c?new SolidColorBrush(c):new SolidColorBrush(Color.FromRgb(47,59,77)));
            Box(offset.X,offset.Y,original.Width,original.Height,Brushes.White,new SolidColorBrush(Color.FromArgb(210,32,34,40)));
            Summary.Text=$"New: {options.Width:N0} × {options.Height:N0} px · {(long)options.Width*options.Height*4/1048576d:0.##} MiB RGBA";
            ErrorLabel.Text="";ApplyButton.IsEnabled=!working;
        }
        catch(Exception e){ErrorLabel.Text=e.Message;ApplyButton.IsEnabled=false;}
    }
    private void Apply(object sender,RoutedEventArgs e)=>PendingApply=ApplyAsync();
    private async Task ApplyAsync()
    {
        if(working||!ApplyButton.IsEnabled)return;
        try
        {
            var options=ReadOptions();working=true;Options.IsEnabled=false;ApplyButton.IsEnabled=false;
            var result=await Task.Run(()=>CanvasResize.Apply(original,options,cancellation.Token));
            cancellation.Token.ThrowIfCancellationRequested();
            if(EditorSession.UndoBytesRequired(original,result)>EditorSession.MaxHistoryBytes)
                throw new InvalidOperationException("The canvas change exceeds the Undo memory limit.");
            ResultDocument=result;DialogResult=true;
        }
        catch(OperationCanceledException){}
        catch(Exception e){ErrorLabel.Text=e.Message;}
        finally{working=false;if(!cancellation.IsCancellationRequested){Options.IsEnabled=true;ApplyButton.IsEnabled=true;}}
    }
    private void Cancel(object sender,RoutedEventArgs e){cancellation.Cancel();Close();}
}
