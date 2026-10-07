using System.Windows;
namespace Compositor.App;
public partial class MainWindow
{
    private void CanvasFileDragOver(object sender,DragEventArgs e)
    {
        e.Handled=true;
        e.Effects=CanDropTabFiles(workspace.Current.Id)&&e.Data.GetDataPresent(DataFormats.FileDrop)&&e.AllowedEffects.HasFlag(DragDropEffects.Copy)
            ?DragDropEffects.Copy:DragDropEffects.None;
    }
    private async void CanvasFileDrop(object sender,DragEventArgs e)
    {
        e.Handled=true;e.Effects=DragDropEffects.None;
        if(!CanDropTabFiles(workspace.Current.Id)||!e.AllowedEffects.HasFlag(DragDropEffects.Copy)||e.Data.GetData(DataFormats.FileDrop) is not string[] paths)return;
        e.Effects=DragDropEffects.Copy;await DropCanvasFiles(paths,e.GetPosition(Canvas));
    }
    private Task DropCanvasFiles(string[] paths,Point screenPoint)
    {
        var target=workspace.Current.Id;var documentPoint=Canvas.DocumentPoint(screenPoint);
        return RouteDroppedFiles(target,paths,documentPoint);
    }
}
