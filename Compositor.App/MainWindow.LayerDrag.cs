using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Compositor.Core;
namespace Compositor.App;
public partial class MainWindow
{
    private const string LayerDragFormat="Compositor.InternalLayerMove";
    private sealed record LayerDragPayload(Document Source,Guid[] Ids);
    private sealed record LayerDropSlot(Guid? Parent,Guid? Above,bool Bottom,Rect Mark,bool Into);
    private Point? layerDragStart;
    private Guid? layerDragPressed;
    private bool layerDragPreserved;
    private LayerDragPayload? layerDragPayload;
    private LayerDropAdorner? layerDropAdorner;
    private void LayerDragDown(object sender,MouseButtonEventArgs e)
    {
        layerDragStart=null;layerDragPressed=null;layerDragPreserved=false;
        if(busy||session.InTransaction||Keyboard.Modifiers!=ModifierKeys.None)return;
        DependencyObject? hit=e.OriginalSource as DependencyObject;
        for(var p=hit;p is not null&&p!=Layers;p=p is Visual?VisualTreeHelper.GetParent(p):(p as FrameworkContentElement)?.Parent)
            if(p is ButtonBase||p is ScrollBar)return;
        if(hit is null)return;
        if(ItemsControl.ContainerFromElement(Layers,hit) is not ListBoxItem {DataContext:LayerRow row})return;
        layerDragStart=e.GetPosition(Layers);layerDragPressed=row.Id;
        if(session.SelectedLayerIds.Contains(row.Id)){layerDragPreserved=true;e.Handled=true;Layers.Focus();}
    }
    private void LayerDragUp(object sender,MouseButtonEventArgs e)
    {
        var clicked=layerDragPressed;bool preserve=layerDragPreserved;
        layerDragStart=null;layerDragPressed=null;layerDragPreserved=false;
        if(preserve&&clicked is {} id&&!busy&&!session.InTransaction){session.SelectLayers(new[]{id},id);e.Handled=true;}
    }
    private void LayerDragMove(object sender,MouseEventArgs e)
    {
        if(layerDragStart is not {} start||e.LeftButton!=MouseButtonState.Pressed||busy||session.InTransaction)return;
        var point=e.GetPosition(Layers);
        if(Math.Abs(point.X-start.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(point.Y-start.Y)<SystemParameters.MinimumVerticalDragDistance)return;
        layerDragStart=null;layerDragPressed=null;layerDragPreserved=false;
        if(session.SelectedLayerIds.Count==0)return;
        var payload=new LayerDragPayload(session.Document,session.SelectedLayerIds.ToArray());layerDragPayload=payload;
        try{DragDrop.DoDragDrop(Layers,new DataObject(LayerDragFormat,payload),DragDropEffects.Move);}
        finally{layerDragPayload=null;ClearLayerDropMark();}
        e.Handled=true;
    }
    private LayerDropSlot DropSlot(Point point)
    {
        var rows=Layers.Items.Cast<LayerRow>().ToArray();
        for(int i=0;i<rows.Length;i++)
        {
            if(Layers.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item)continue;
            var origin=item.TranslatePoint(new Point(),Layers);double h=item.ActualHeight;
            if(point.Y>origin.Y+h)continue;
            var row=rows[i];double fraction=h>0?(point.Y-origin.Y)/h:0;
            if(row.Layer.IsGroup&&fraction>=.25&&fraction<=.75)
                return new(row.Id,null,false,new Rect(origin.X,origin.Y,item.ActualWidth,h),true);
            if(fraction>.5)
            {
                if(i+1<rows.Length)return new(rows[i+1].Layer.ParentId,rows[i+1].Id,false,new Rect(0,origin.Y+h,Layers.ActualWidth,0),false);
                break;
            }
            return new(row.Layer.ParentId,row.Id,false,new Rect(0,Math.Max(0,origin.Y),Layers.ActualWidth,0),false);
        }
        return new(null,null,true,new Rect(0,Math.Min(point.Y,Layers.ActualHeight-2),Layers.ActualWidth,0),false);
    }
    private Document PrepareLayerDrop(LayerDragPayload payload,LayerDropSlot slot)
    {
        if(busy||session.InTransaction||!ReferenceEquals(payload.Source,session.Document))throw new InvalidOperationException("The layer drag is no longer current.");
        return LayerHierarchy.PlaceSelected(session.Document,payload.Ids,slot.Parent,slot.Above,slot.Bottom);
    }
    private bool OwnLayerDrag(DragEventArgs e)=>layerDragPayload is not null&&e.Data.GetDataPresent(LayerDragFormat)&&ReferenceEquals(e.Data.GetData(LayerDragFormat),layerDragPayload);
    private void LayerDragOver(object sender,DragEventArgs e)
    {
        if(!OwnLayerDrag(e))return;
        e.Handled=true;e.Effects=DragDropEffects.None;
        try
        {
            var slot=DropSlot(e.GetPosition(Layers));PrepareLayerDrop(layerDragPayload!,slot);
            ShowLayerDropMark(slot);e.Effects=DragDropEffects.Move;
        }
        catch(InvalidOperationException){ClearLayerDropMark();}
        catch(InvalidDataException){ClearLayerDropMark();}
    }
    private void LayerDragLeave(object sender,DragEventArgs e){if(OwnLayerDrag(e))ClearLayerDropMark();}
    private void DropLayerRows(object sender,DragEventArgs e)
    {
        if(!OwnLayerDrag(e))return;
        e.Handled=true;e.Effects=DragDropEffects.None;ClearLayerDropMark();
        try
        {
            var slot=DropSlot(e.GetPosition(Layers));var next=PrepareLayerDrop(layerDragPayload!,slot);
            CommitLayerDrop(next,slot);e.Effects=DragDropEffects.Move;
        }
        catch(Exception error){ShowError(error.Message);}
    }
    private void CommitLayerDrop(Document next,LayerDropSlot slot)
    {
        session.Apply(_=>next);if(slot.Parent is {} parent)collapsedGroups.Remove(parent);Refresh();
    }
    private void ShowLayerDropMark(LayerDropSlot slot)
    {
        if(layerDropAdorner is null&&AdornerLayer.GetAdornerLayer(Layers) is {} host)
        {layerDropAdorner=new(Layers){IsHitTestVisible=false};host.Add(layerDropAdorner);}
        if(layerDropAdorner is {} mark){mark.Mark=slot.Mark;mark.Into=slot.Into;mark.InvalidateVisual();}
    }
    private void ClearLayerDropMark()
    {
        if(layerDropAdorner is {} mark){AdornerLayer.GetAdornerLayer(Layers)?.Remove(mark);layerDropAdorner=null;}
    }
    private sealed class LayerDropAdorner(UIElement element):Adorner(element)
    {
        public Rect Mark;public bool Into;
        protected override void OnRender(DrawingContext context)
        {
            var pen=new Pen(new SolidColorBrush(Color.FromRgb(108,154,224)),2);
            if(Into)context.DrawRectangle(new SolidColorBrush(Color.FromArgb(35,108,154,224)),pen,Mark);
            else context.DrawLine(pen,Mark.TopLeft,Mark.TopRight);
        }
    }
}