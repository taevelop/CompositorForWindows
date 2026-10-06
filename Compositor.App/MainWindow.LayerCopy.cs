using System.Windows;
using Compositor.Imaging;
using Compositor.Core;
namespace Compositor.App;
public partial class MainWindow
{
    private void LayerViaCopy(object? sender,RoutedEventArgs e)=>Safe(()=>{LayerCopy.ViaSelection(session);Canvas.Focus();});
    private void DuplicateLayer(object? sender,RoutedEventArgs e)=>Safe(()=>
    {
        if(session.ActiveLayerId is not {} active)return;
        var ids=LayerHierarchy.Subtree(session.Document,active);
        var originals=session.Document.Layers.Where(l=>ids.Contains(l.Id)).ToArray();
        if(LayerCopy.Duplicate(session) is {} copied)
        {
            var copiedIds=LayerHierarchy.Subtree(session.Document,copied);
            var copies=session.Document.Layers.Where(l=>copiedIds.Contains(l.Id)).ToArray();
            for(int i=0;i<originals.Length;i++)if(collapsedGroups.Contains(originals[i].Id))collapsedGroups.Add(copies[i].Id);
            RefreshHierarchy();
        }
        Canvas.Focus();
    });
}
