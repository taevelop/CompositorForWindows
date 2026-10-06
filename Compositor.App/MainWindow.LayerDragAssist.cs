using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace Compositor.App;
public partial class MainWindow
{
    private DispatcherTimer? layerDragAssistTimer;
    private Point? layerDragAssistPoint;
    private bool layerDragAssistCopy;
    private long layerDragScrollAt,layerDragHoverAt;
    private Guid? layerDragHoverFolder;
    private void TrackLayerDrag(Point point,bool copy,long now)
    {
        if(layerDragAssistPoint is null)layerDragScrollAt=now;
        layerDragAssistPoint=point;layerDragAssistCopy=copy;
        if(layerDragAssistTimer is null)
        {
            layerDragAssistTimer=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(50)};
            layerDragAssistTimer.Tick+=(_,_)=>TickLayerDragAssist(Environment.TickCount64);
        }
        layerDragAssistTimer.Start();
    }
    private void SetLayerDragHover(Guid? folder,long now)
    {
        if(layerDragHoverFolder==folder)return;
        layerDragHoverFolder=folder;layerDragHoverAt=now;
    }
    private bool RefreshLayerDragTarget(long now)
    {
        if(layerDragPayload is not {} payload||layerDragAssistPoint is not {} point)return false;
        try
        {
            var slot=DropSlot(point);
            if(layerDragAssistCopy)PrepareLayerCopyDrop(payload,slot);else PrepareLayerDrop(payload,slot);
            ShowLayerDropMark(slot);
            SetLayerDragHover(slot.Into&&slot.Parent is {} id&&collapsedGroups.Contains(id)?id:null,now);
            return true;
        }
        catch(InvalidOperationException){ClearLayerDropMark();SetLayerDragHover(null,now);return false;}
        catch(InvalidDataException){ClearLayerDropMark();SetLayerDragHover(null,now);return false;}
    }
    private static T? LayerDragVisual<T>(DependencyObject node) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
        {
            var child=VisualTreeHelper.GetChild(node,i);
            if(child is T found)return found;
            if(LayerDragVisual<T>(child) is {} nested)return nested;
        }
        return null;
    }
    private void TickLayerDragAssist(long now)
    {
        if(layerDragPayload is not {} payload||layerDragAssistPoint is not {} point||busy||session.InTransaction||!ReferenceEquals(payload.Source,session.Document))
        {StopLayerDragAssist();ClearLayerDropMark();return;}
        if(point.X<0||point.X>Layers.ActualWidth||point.Y<0||point.Y>Layers.ActualHeight)
        {StopLayerDragAssist();ClearLayerDropMark();return;}
        if(now-layerDragScrollAt>=100&&LayerDragVisual<ScrollViewer>(Layers) is {} scroll)
        {
            layerDragScrollAt=now;double before=scroll.VerticalOffset;
            if(point.Y<24)scroll.LineUp();else if(point.Y>Layers.ActualHeight-24)scroll.LineDown();
            Layers.UpdateLayout();
            if(scroll.VerticalOffset!=before){SetLayerDragHover(null,now);RefreshLayerDragTarget(now);return;}
        }
        if(layerDragHoverFolder is {} folder&&now-layerDragHoverAt>=650)
        {
            collapsedGroups.Remove(folder);SetLayerDragHover(null,now);
            RefreshHierarchy();Layers.UpdateLayout();RefreshLayerDragTarget(now);
        }
    }
    private void StopLayerDragAssist()
    {
        layerDragAssistTimer?.Stop();layerDragAssistPoint=null;layerDragHoverFolder=null;
    }
    private void LayerDragContinue(object sender,QueryContinueDragEventArgs e)
    {
        if(e.EscapePressed||e.Action!=DragAction.Continue||!e.KeyStates.HasFlag(DragDropKeyStates.LeftMouseButton))
        {StopLayerDragAssist();ClearLayerDropMark();}
    }
}