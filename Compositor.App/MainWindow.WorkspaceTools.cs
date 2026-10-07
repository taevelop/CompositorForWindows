using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Compositor.Core;
namespace Compositor.App;
public partial class MainWindow
{
    private sealed record TabTools(int Tool,string Size,string Hardness,string Opacity,string Color,string Mask,
        Compositor.Core.SelectionMode Mode,bool Antialiased,bool Center,int CropRatio,bool CropSnap,string Background);
    private TabTools defaultTabTools=null!;
    private TabTools CaptureTabTools()=>new(ToolPicker.SelectedIndex,BrushSize.Text,BrushHardness.Text,BrushOpacity.Text,
        BrushColor.Text,MaskGray.Text,Canvas.SelectionMode,Canvas.SelectionAntialiased,Canvas.SelectionFromCenter,
        CropRatioPicker.SelectedIndex,Canvas.CropSnapEnabled,backgroundColor);
    private void RestoreTabTools(TabTools state)
    {
        ToolPicker.SelectedIndex=state.Tool;
        BrushSize.Text=state.Size;BrushHardness.Text=state.Hardness;BrushOpacity.Text=state.Opacity;
        BrushColor.Text=state.Color;MaskGray.Text=state.Mask;SetBackgroundColor(state.Background);
        foreach(var radio in SelectionOptions.Children.OfType<RadioButton>())radio.IsChecked=radio.Tag?.ToString()==((int)state.Mode).ToString();
        Canvas.SelectionMode=state.Mode;
        SelectionAA.IsChecked=Canvas.SelectionAntialiased=state.Antialiased;
        SelectionCenter.IsChecked=Canvas.SelectionFromCenter=state.Center;
        CropRatioPicker.SelectedIndex=state.CropRatio;CropSnap.IsChecked=Canvas.CropSnapEnabled=state.CropSnap;
        Canvas.CancelCrop();RefreshToolControls();
    }
    private async void CloseDocument(object sender,RoutedEventArgs e)=>await CloseTab(workspace.Current.Id);
    private void NextDocument(object sender,RoutedEventArgs e)=>CycleDocumentTab(false);
    private void PreviousDocument(object sender,RoutedEventArgs e)=>CycleDocumentTab(true);
    private void CycleDocumentTab(bool backwards)
    {
        var tabs=workspace.Documents;int index=tabs.IndexOf(workspace.Current);
        SelectTab(tabs[(index+(backwards?-1:1)+tabs.Count)%tabs.Count].Id);
    }
    private async Task<bool> HandleDocumentShortcut(Key key,ModifierKeys modifiers)
    {
        if(modifiers is not (ModifierKeys.Control or (ModifierKeys.Control|ModifierKeys.Shift)))return false;
        if(key==Key.Tab){if(!busy)CycleDocumentTab(modifiers.HasFlag(ModifierKeys.Shift));return true;}
        if(key==Key.W&&modifiers==ModifierKeys.Control){if(!busy)await CloseTab(workspace.Current.Id);return true;}
        return false;
    }
}
