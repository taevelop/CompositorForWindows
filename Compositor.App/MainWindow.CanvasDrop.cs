using System.Windows;
namespace Compositor.App;
public partial class MainWindow
{
    private void CanvasFileDragOver(object sender,DragEventArgs e)
    {
        e.Handled=true;
        e.Effects=e.AllowedEffects.HasFlag(DragDropEffects.Copy)&&((CanDropTabFiles(workspace.Current.Id)&&e.Data.GetDataPresent(DataFormats.FileDrop))||CanTransferTabLayers(workspace.Current.Id,e.Data,System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)))
            ?DragDropEffects.Copy:DragDropEffects.None;
    }
    private async void CanvasFileDrop(object sender,DragEventArgs e)
    {
        StopTabHover();e.Handled=true;e.Effects=DragDropEffects.None;
        if(e.AllowedEffects.HasFlag(DragDropEffects.Copy)&&CanTransferTabLayers(workspace.Current.Id,e.Data,System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)))
        {
            try{TransferTabLayers(workspace.Current.Id,e.Data,false,Canvas.DocumentPoint(e.GetPosition(Canvas)));e.Effects=DragDropEffects.Copy;}
            catch(Exception error){ShowError(error.Message);}
            return;
        }
        if(!CanDropTabFiles(workspace.Current.Id)||!e.AllowedEffects.HasFlag(DragDropEffects.Copy)||e.Data.GetData(DataFormats.FileDrop) is not string[] paths)return;
        e.Effects=DragDropEffects.Copy;await DropCanvasFiles(paths,e.GetPosition(Canvas));
    }
    private Task DropCanvasFiles(string[] paths,Point screenPoint)
    {
        var target=workspace.Current.Id;var documentPoint=Canvas.DocumentPoint(screenPoint);
        return RouteDroppedFiles(target,paths,documentPoint);
    }
}
