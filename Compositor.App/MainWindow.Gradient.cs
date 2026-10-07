using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    private (int Digit,double Time)? gradientOpacityDigit;
    private async Task<bool> HandleGradientShortcut(Key key,ModifierKeys modifiers,double? opacityTime=null)
    {
        if(busy)return false;
        if(Canvas.Tool==EditorTool.Gradient&&modifiers==ModifierKeys.None)
        {
            int digit=key is >=Key.D0 and <=Key.D9?(int)key-(int)Key.D0:key is >=Key.NumPad0 and <=Key.NumPad9?(int)key-(int)Key.NumPad0:-1;
            if(digit>=0)
            {
                double now=opacityTime??(double)System.Diagnostics.Stopwatch.GetTimestamp()/System.Diagnostics.Stopwatch.Frequency;
                int percent=digit==0?100:digit*10;
                if(gradientOpacityDigit is {} previous&&now>=previous.Time&&now-previous.Time<.6)
                {percent=Math.Max(1,previous.Digit*10+digit);gradientOpacityDigit=null;}
                else gradientOpacityDigit=(digit,now);
                GradientOpacity.Value=percent;return true;
            }
        }
        if(!Canvas.HasGradient)return false;
        if(key==Key.S&&modifiers is ModifierKeys.Control or (ModifierKeys.Control|ModifierKeys.Shift))
        {await Save(modifiers.HasFlag(ModifierKeys.Shift));return true;}
        if(modifiers is not (ModifierKeys.None or ModifierKeys.Shift))return false;
        int tool=key switch
        {
            Key.V=>0,Key.B=>1,Key.E=>2,Key.H=>3,
            Key.M=>modifiers.HasFlag(ModifierKeys.Shift)?5:4,
            Key.L=>modifiers.HasFlag(ModifierKeys.Shift)?7:6,
            Key.C=>8,Key.I=>9,Key.G=>10,Key.U=>11,_=>-1
        };
        if(tool<0)return false;
        if(tool==ToolPicker.SelectedIndex)return true;
        if(await ResolveGradientBeforeAction())ToolPicker.SelectedIndex=tool;
        return true;
    }
    private async Task<bool> ResolveGradientBeforeAction()
    {
        if (busy) return false;
        if (!Canvas.HasGradient) return true;
        busy=true;Editor.IsEnabled=false;
        try { return await Canvas.CommitGradientAsync(); }
        finally { busy=false;Editor.IsEnabled=true;Refresh(); }
    }
    private GradientFillSettings ReadGradient(PointD start,PointD end)
    {
        ColorPickerWindow.TryHex(BrushColor.Text,out var foreground);ColorPickerWindow.TryHex(backgroundColor,out var background);
        if(session.EditMask){byte value=(byte)Math.Round(MaskSlider.Value*255/100);foreground=System.Windows.Media.Color.FromRgb(value,value,value);value=(byte)(255-value);background=System.Windows.Media.Color.FromRgb(value,value,value);}
        return new(start,end,foreground.R,foreground.G,foreground.B,background.R,background.G,background.B,
            GradientRadial.IsChecked==true?GradientShape.Radial:GradientShape.Linear,
            GradientTransparent.IsChecked==true?GradientStyle.ForegroundToTransparent:GradientStyle.ForegroundToBackground,
            GradientReverse.IsChecked==true,GradientOpacity.Value/100);
    }
    private void RefreshGradientSwatch()
    {
        if(GradientSwatch is null||GradientOpacity is null)return;
        var settings=ReadGradient(new(0,0),new(1,0));
        var first=System.Windows.Media.Color.FromRgb(settings.ForegroundRed,settings.ForegroundGreen,settings.ForegroundBlue);
        var last=settings.Style==GradientStyle.ForegroundToTransparent
            ?System.Windows.Media.Color.FromArgb(0,first.R,first.G,first.B)
            :System.Windows.Media.Color.FromRgb(settings.BackgroundRed,settings.BackgroundGreen,settings.BackgroundBlue);
        if(settings.Reversed)(first,last)=(last,first);
        GradientSwatch.Background=new System.Windows.Media.LinearGradientBrush(first,last,0);
    }
    private void GradientPaletteChanged(){RefreshGradientSwatch();Canvas?.UpdateGradient();}
    private void GradientChanged(object sender,RoutedEventArgs e){if(IsInitialized)GradientPaletteChanged();}
    private async void ApplyGradient(object? sender,RoutedEventArgs e){await Canvas.CommitGradientAsync();Refresh();}
    private void CancelGradient(object? sender,RoutedEventArgs e){Canvas.CancelGradient();Refresh();}
}
