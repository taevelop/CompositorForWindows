using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Compositor.Core;
namespace Compositor.App;
public sealed class SelectedAppearanceWindow:Window
{
    internal readonly CheckBox ChangeOpacity=new(){Content="Change opacity"};
    internal readonly Slider OpacityValue=new(){Minimum=0,Maximum=100,Width=230,Margin=new(0,8,12,10)};
    internal readonly TextBox OpacityNumber=new(){Width=64,Margin=new(0,4,0,8)};
    internal readonly CheckBox ChangeVisibility=new(){Content="Change visibility",Margin=new(0,14,0,6)};
    internal readonly CheckBox VisibleValue=new(){Content="Visible",Margin=new(18,0,0,8)};
    internal readonly CheckBox ChangeBlend=new(){Content="Change blend mode",Margin=new(0,14,0,6)};
    internal readonly ComboBox BlendValue=new(){MinWidth=220,Margin=new(18,0,0,8)};
    private readonly TextBlock error=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,10,0,4)};
    private readonly Button apply=new(){Content="Apply",MinWidth=80,IsDefault=true};
    private readonly EditorSession session;
    private readonly Document original;
    private readonly Guid[] selection;
    private bool updating,accepted;
    public SelectedAppearanceWindow(EditorSession session)
    {
        this.session=session;original=session.Document;selection=session.SelectedLayerIds.ToArray();
        var layers=original.Layers.Where(l=>selection.Contains(l.Id)).ToArray();
        if(layers.Length==0)throw new InvalidOperationException("Select layers to edit.");
        Style=(Style)FindResource(typeof(Window));Title="Selected layer appearance";Width=410;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new(20)};
        panel.Children.Add(new TextBlock{Text=$"{layers.Length} selected layers · check only the properties to change",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,16)});
        panel.Children.Add(ChangeOpacity);var opacityRow=new StackPanel{Orientation=Orientation.Horizontal};opacityRow.Children.Add(OpacityValue);opacityRow.Children.Add(OpacityNumber);panel.Children.Add(opacityRow);
        panel.Children.Add(new TextBlock{Text=layers.Select(l=>l.Opacity).Distinct().Count()>1?"Current opacity: mixed":"Current opacity: "+(layers[0].Opacity*100).ToString("0.###",CultureInfo.InvariantCulture)+"%"});
        panel.Children.Add(ChangeVisibility);panel.Children.Add(VisibleValue);panel.Children.Add(ChangeBlend);panel.Children.Add(BlendValue);
        if(layers.Any(l=>l.IsGroup))panel.Children.Add(new TextBlock{Text="Folders selected: blending is unavailable. Folder and child opacity are both changed if both are selected.",TextWrapping=TextWrapping.Wrap,Margin=new(0,4,0,4)});
        panel.Children.Add(error);var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,12,0,0)};
        var cancel=new Button{Content="Cancel",IsCancel=true,MinWidth=80,Margin=new(0,0,10,0)};buttons.Children.Add(cancel);buttons.Children.Add(apply);panel.Children.Add(buttons);Content=panel;
        OpacityValue.Style=(Style)FindResource("EditorSlider");OpacityValue.Value=layers[0].Opacity*100;OpacityNumber.Text=OpacityValue.Value.ToString("0.###",CultureInfo.InvariantCulture);
        VisibleValue.IsThreeState=true;VisibleValue.IsChecked=layers.Select(l=>l.Visible).Distinct().Count()==1?layers[0].Visible:null;
        BlendValue.ItemsSource=Enum.GetValues<BlendMode>();BlendValue.SelectedItem=layers.Select(l=>l.Blend).Distinct().Count()==1?layers[0].Blend:null;
        ChangeBlend.IsEnabled=!layers.Any(l=>l.IsGroup);
        session.Begin();
        foreach(var check in new[]{ChangeOpacity,ChangeVisibility,ChangeBlend,VisibleValue}){check.Foreground=(System.Windows.Media.Brush)FindResource("Ink");check.Checked+=(_,_)=>UpdatePreview();check.Unchecked+=(_,_)=>UpdatePreview();check.Indeterminate+=(_,_)=>UpdatePreview();}
        OpacityValue.ValueChanged+=(_,_)=>{if(updating)return;updating=true;OpacityNumber.Text=OpacityValue.Value.ToString("0.###",CultureInfo.InvariantCulture);updating=false;UpdatePreview();};
        OpacityNumber.TextChanged+=(_,_)=>{if(updating)return;updating=true;if(double.TryParse(OpacityNumber.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)&&double.IsFinite(n)&&n>=0&&n<=100)OpacityValue.Value=n;updating=false;UpdatePreview();};
        BlendValue.SelectionChanged+=(_,_)=>UpdatePreview();
        apply.Click+=(_,_)=>Finish(true);
        Closed+=(_,_)=>{if(accepted)session.Commit();else session.Cancel();};
        UpdatePreview();
    }
    internal void Finish(bool commit)
    {
        if(commit&&!UpdatePreview())return;
        accepted=commit;Close();
    }
    internal bool UpdatePreview()
    {
        OpacityValue.IsEnabled=OpacityNumber.IsEnabled=ChangeOpacity.IsChecked==true;
        VisibleValue.IsEnabled=ChangeVisibility.IsChecked==true;BlendValue.IsEnabled=ChangeBlend.IsEnabled&&ChangeBlend.IsChecked==true;
        try
        {
            double? opacity=null;bool? visible=null;BlendMode? blend=null;
            if(ChangeOpacity.IsChecked==true)
            {
                if(!double.TryParse(OpacityNumber.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double n))throw new InvalidOperationException("Enter an opacity from 0 to 100.");opacity=n/100;
            }
            if(ChangeVisibility.IsChecked==true)visible=VisibleValue.IsChecked??throw new InvalidOperationException("Choose visible or hidden.");
            if(ChangeBlend.IsChecked==true)blend=BlendValue.SelectedItem is BlendMode mode?mode:throw new InvalidOperationException("Choose a blend mode.");
            session.Preview(LayerAppearanceEdit.Apply(original,selection,opacity,visible,blend));error.Text="";
            apply.IsEnabled=opacity is not null||visible is not null||blend is not null;return apply.IsEnabled;
        }
        catch(Exception exception){session.Preview(original);error.Text=exception.Message;apply.IsEnabled=false;return false;}
    }
}