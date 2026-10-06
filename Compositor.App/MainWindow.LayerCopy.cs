using System.Windows;
using Compositor.Imaging;
using Compositor.Core;
namespace Compositor.App;
public partial class MainWindow
{
    private void LayerViaCopy(object? sender,RoutedEventArgs e)=>Safe(()=>{LayerCopy.ViaSelection(session);Canvas.Focus();});
    private void DuplicateLayer(object? sender,RoutedEventArgs e)=>Safe(()=>
    {
        var mapping=LayerCopy.DuplicateSelected(session);
        foreach(var pair in mapping)
            if(collapsedGroups.Contains(pair.Key))collapsedGroups.Add(pair.Value);
        RefreshHierarchy();
        Canvas.Focus();
    });
}
