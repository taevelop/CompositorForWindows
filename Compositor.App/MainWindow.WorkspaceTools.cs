using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Compositor.Core;
namespace Compositor.App;
public partial class MainWindow
{
    private sealed record TabTools(int Tool,string Size,string Hardness,string Opacity,string Color,string Mask,
        Compositor.Core.SelectionMode Mode,bool Antialiased,bool Center,int CropRatio,bool CropSnap,string Background,bool SampleRing,
        bool GradientRadial,bool GradientTransparent,bool GradientReverse,double GradientOpacity,ShapeKind ShapeKind,string ShapeRadius,string ShapeWidth);
    private TabTools defaultTabTools=null!;
    private TabTools CaptureTabTools()=>new(ToolPicker.SelectedIndex,BrushSize.Text,BrushHardness.Text,BrushOpacity.Text,
        BrushColor.Text,MaskGray.Text,Canvas.SelectionMode,Canvas.SelectionAntialiased,Canvas.SelectionFromCenter,
        CropRatioPicker.SelectedIndex,Canvas.CropSnapEnabled,backgroundColor,Canvas.ShowSampleRing,
        GradientRadial.IsChecked==true,GradientTransparent.IsChecked==true,GradientReverse.IsChecked==true,GradientOpacity.Value,ReadShape().Kind,ShapeRadiusValue.Text,ShapeWidthValue.Text);
    private void RestoreTabTools(TabTools state)
    {
        gradientOpacityDigit=null;
        ShapeRectangle.IsChecked=state.ShapeKind==ShapeKind.Rectangle;ShapeEllipse.IsChecked=state.ShapeKind==ShapeKind.Ellipse;ShapeLine.IsChecked=state.ShapeKind==ShapeKind.Line;
        ShapeRadiusValue.Text=state.ShapeRadius;ShapeWidthValue.Text=state.ShapeWidth;
        ToolPicker.SelectedIndex=state.Tool;
        SampleRingToggle.IsChecked=Canvas.ShowSampleRing=state.SampleRing;
        BrushSize.Text=state.Size;BrushHardness.Text=state.Hardness;BrushOpacity.Text=state.Opacity;
        BrushColor.Text=state.Color;MaskGray.Text=state.Mask;SetBackgroundColor(state.Background);
        GradientLinear.IsChecked=!state.GradientRadial;GradientRadial.IsChecked=state.GradientRadial;
        GradientTransparent.IsChecked=state.GradientTransparent;GradientReverse.IsChecked=state.GradientReverse;GradientOpacity.Value=state.GradientOpacity;
        foreach(var radio in SelectionOptions.Children.OfType<RadioButton>())radio.IsChecked=radio.Tag?.ToString()==((int)state.Mode).ToString();
        Canvas.SelectionMode=state.Mode;
        SelectionAA.IsChecked=Canvas.SelectionAntialiased=state.Antialiased;
        SelectionCenter.IsChecked=Canvas.SelectionFromCenter=state.Center;
        CropRatioPicker.SelectedIndex=state.CropRatio;CropSnap.IsChecked=Canvas.CropSnapEnabled=state.CropSnap;
        Canvas.CancelCrop();RefreshToolControls();
    }
    private async void CloseDocument(object sender,RoutedEventArgs e)=>await CloseTab(workspace.Current.Id);
    private async void NextDocument(object sender,RoutedEventArgs e)=>await CycleDocumentTab(false);
    private async void PreviousDocument(object sender,RoutedEventArgs e)=>await CycleDocumentTab(true);
    private async Task CycleDocumentTab(bool backwards)
    {
        var tabs=workspace.Documents;int index=tabs.IndexOf(workspace.Current);
        await SelectTabAsync(tabs[(index+(backwards?-1:1)+tabs.Count)%tabs.Count].Id);
    }
    private async Task<bool> HandleDocumentShortcut(Key key,ModifierKeys modifiers)
    {
        if(modifiers is not (ModifierKeys.Control or (ModifierKeys.Control|ModifierKeys.Shift)))return false;
        if(key==Key.Tab){if(!busy)await CycleDocumentTab(modifiers.HasFlag(ModifierKeys.Shift));return true;}
        if(key==Key.W&&modifiers==ModifierKeys.Control){if(!busy)await CloseTab(workspace.Current.Id);return true;}
        return false;
    }
}
