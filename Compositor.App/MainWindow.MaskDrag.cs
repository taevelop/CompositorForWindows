using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;

public partial class MainWindow
{
    private const string MaskDragFormat="Compositor.InternalMaskCopy";
    private sealed record MaskDragPayload(Document Source,Guid LayerId);
    private Point? maskThumbStart;
    private Guid? maskThumbSource;
    private MaskDragPayload? maskDragPayload;
    private bool maskDragConsumed;
    private void MaskThumbDown(object sender,MouseButtonEventArgs e)
    {
        maskThumbStart=null;maskThumbSource=null;maskDragConsumed=false;
        if(busy||session.InTransaction||!Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)||Keyboard.Modifiers.HasFlag(ModifierKeys.Control))return;
        if(sender is Button {DataContext:LayerRow row}&&row.Layer.Mask is not null){maskThumbStart=e.GetPosition(Layers);maskThumbSource=row.Id;}
    }
    private void MaskThumbUp(object sender,MouseButtonEventArgs e){maskThumbStart=null;maskThumbSource=null;}
    private void MaskThumbMove(object sender,MouseEventArgs e)
    {
        if(maskThumbStart is not {} start||maskThumbSource is not {} id||e.LeftButton!=MouseButtonState.Pressed||busy||session.InTransaction)return;
        var point=e.GetPosition(Layers);if(Math.Abs(point.X-start.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(point.Y-start.Y)<SystemParameters.MinimumVerticalDragDistance)return;
        maskThumbStart=null;maskThumbSource=null;maskDragConsumed=true;maskDragPayload=new(session.Document,id);
        try{DragDrop.DoDragDrop((DependencyObject)sender,new DataObject(MaskDragFormat,maskDragPayload),DragDropEffects.Copy);}
        finally{maskDragPayload=null;ClearLayerDropMark();}
        e.Handled=true;
    }
    private bool OwnMaskDrag(DragEventArgs e)=>maskDragPayload is not null&&e.Data.GetDataPresent(MaskDragFormat)&&ReferenceEquals(e.Data.GetData(MaskDragFormat),maskDragPayload);
    private (LayerRow Row,Rect Bounds)? MaskDropTarget(Point point)
    {
        foreach(var row in Layers.Items.Cast<LayerRow>())if(Layers.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem item)
        {
            var bounds=new Rect(item.TranslatePoint(new(0,0),Layers),new Size(item.ActualWidth,item.ActualHeight));if(bounds.Contains(point))return(row,bounds);
        }
        return null;
    }
    private bool CanDropMask(MaskDragPayload payload,LayerRow target)=>!busy&&!session.InTransaction&&ReferenceEquals(session.Document,payload.Source)&&payload.LayerId!=target.Id&&!target.Layer.IsGroup&&payload.Source.Layers.FirstOrDefault(l=>l.Id==payload.LayerId)?.Mask is not null;
    private void MaskDragOver(DragEventArgs e)
    {
        e.Handled=true;e.Effects=DragDropEffects.None;
        if(MaskDropTarget(e.GetPosition(Layers)) is {} target&&CanDropMask(maskDragPayload!,target.Row))
        {e.Effects=DragDropEffects.Copy;ShowLayerDropMark(new(null,null,false,target.Bounds,true));}
        else ClearLayerDropMark();
    }
    private bool CommitMaskCopy(MaskDragPayload payload,Guid target)
    {
        if(busy||session.InTransaction||!ReferenceEquals(session.Document,payload.Source)||payload.LayerId==target)return false;
        var next=LayerMaskCopy.Apply(session.Document,payload.LayerId,target);session.Apply(_=>next);session.SelectLayers(new[]{target},target);session.EditMask=true;Refresh();return true;
    }
    private void DropMask(DragEventArgs e)
    {
        e.Handled=true;e.Effects=DragDropEffects.None;ClearLayerDropMark();
        try{if(MaskDropTarget(e.GetPosition(Layers)) is {} target&&CanDropMask(maskDragPayload!,target.Row)&&CommitMaskCopy(maskDragPayload!,target.Row.Id))e.Effects=DragDropEffects.Copy;}
        catch(Exception error){ShowError(error.Message);}
    }
}
