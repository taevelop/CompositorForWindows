using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class ImageSizeWindow:Window
{
    private readonly Document original;
    private readonly ImageSizeDraft draft;
    private readonly CancellationTokenSource cancellation=new();
    private bool updating=true,working;
    public Document? ResultDocument {get;private set;}
    internal Task PendingApply {get;private set;}=Task.CompletedTask;
    public ImageSizeWindow(Document document)
    {
        original=document;draft=new(document);InitializeComponent();
        CurrentLabel.Text=$"Current: {document.Width:N0} × {document.Height:N0} px · {document.Resolution:0.###} ppi";
        SamplingPicker.ItemsSource=Enum.GetValues<Sampling>();SamplingPicker.SelectedItem=Sampling.High;
        Sync();Closed+=(_,_)=>cancellation.Cancel();
    }
    private static string F(double value)=>value.ToString("0.###",CultureInfo.InvariantCulture);
    private static double Number(TextBox field)
    {
        if(!double.TryParse(field.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double value)||!double.IsFinite(value)||value<=0)
            throw new InvalidOperationException("Enter positive numeric dimensions and resolution.");
        return value;
    }
    private void Sync(TextBox? preserve=null)
    {
        updating=true;
        Units.ItemsSource=draft.Resample?Enum.GetValues<CanvasUnit>():new[]{CanvasUnit.Inches,CanvasUnit.Centimeters};
        Units.SelectedItem=draft.Unit;
        if(preserve!=WidthField)WidthField.Text=F(draft.Displayed(true));
        if(preserve!=HeightField)HeightField.Text=F(draft.Displayed(false));
        if(preserve!=ResolutionField)ResolutionField.Text=F(draft.Resolution);
        Locked.IsChecked=draft.Locked;Locked.IsEnabled=draft.Resample;
        SamplingPanel.Visibility=draft.Resample?Visibility.Visible:Visibility.Collapsed;
        Hint.Text=draft.Resample?"Resizes each layer and applies its transforms. Undo restores the originals.":"Only print dimensions and resolution change. Pixels stay unchanged.";
        updating=false;ValidateFields();
    }
    private void ValidateFields()
    {
        try
        {
            Number(WidthField);Number(HeightField);Number(ResolutionField);
            var options=draft.Options();Summary.Text=$"Result: {options.Width:N0} × {options.Height:N0} px · {options.Resolution:0.###} ppi";
            ErrorLabel.Text="";ApplyButton.IsEnabled=!working;
        }
        catch(Exception e){ErrorLabel.Text=e.Message;ApplyButton.IsEnabled=false;}
    }
    private void Edit(TextBox field,Action<double> setter)
    {
        if(updating)return;
        try{setter(Number(field));Sync(field);}
        catch(Exception e){ErrorLabel.Text=e.Message;ApplyButton.IsEnabled=false;}
    }
    private void WidthChanged(object sender,TextChangedEventArgs e)=>Edit(WidthField,v=>draft.SetDimension(v,true));
    private void HeightChanged(object sender,TextChangedEventArgs e)=>Edit(HeightField,v=>draft.SetDimension(v,false));
    private void ResolutionChanged(object sender,TextChangedEventArgs e)=>Edit(ResolutionField,draft.SetResolution);
    private void UnitsChanged(object sender,SelectionChangedEventArgs e){if(updating||Units.SelectedItem is not CanvasUnit unit)return;draft.SetUnit(unit);Sync();}
    private void LockChanged(object sender,RoutedEventArgs e){draft.Locked=Locked.IsChecked==true;ValidateFields();}
    private void ResampleChanged(object sender,RoutedEventArgs e){draft.SetResample(Resample.IsChecked==true);Sync();}
    private void SamplingChanged(object sender,SelectionChangedEventArgs e){if(updating||SamplingPicker.SelectedItem is not Sampling sampling)return;draft.Sampling=sampling;ValidateFields();}
    private void Apply(object sender,RoutedEventArgs e)=>PendingApply=ApplyAsync();
    private async Task ApplyAsync()
    {
        if(working||!ApplyButton.IsEnabled)return;
        try
        {
            var options=draft.Options();working=true;OptionsPanel.IsEnabled=false;ApplyButton.IsEnabled=false;Progress.Visibility=Visibility.Visible;
            var result=await Task.Run(()=>ImageResize.Apply(original,options,cancellation.Token));
            cancellation.Token.ThrowIfCancellationRequested();ResultDocument=result;DialogResult=true;
        }
        catch(OperationCanceledException){}
        catch(Exception e){ErrorLabel.Text=e.Message;}
        finally
        {
            working=false;
            if(!cancellation.IsCancellationRequested){OptionsPanel.IsEnabled=true;ApplyButton.IsEnabled=true;Progress.Visibility=Visibility.Collapsed;}
        }
    }
    private void Cancel(object sender,RoutedEventArgs e){cancellation.Cancel();Close();}
}
