using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Compositor.Core;
namespace Compositor.App;
public partial class MainWindow
{
    private void LayerDragSmokeTest(string path)
    {
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        var d=Document.Create(12,12);var a=d.Layers[0];var b=Layer.Blank("Second",12,12);var folder=Layer.Group("Drop target",12,12);
        d=d with{Layers=d.Layers.Add(b).Add(folder)};session.Load(d,a.Id);session.SelectLayers(new[]{a.Id,b.Id},b.Id);UpdateLayout();
        var row=Layers.Items.Cast<LayerRow>().Single(r=>r.Id==folder.Id);
        var item=(ListBoxItem)Layers.ItemContainerGenerator.ContainerFromItem(row);var origin=item.TranslatePoint(new Point(),Layers);
        var slot=DropSlot(new Point(origin.X+50,origin.Y+item.ActualHeight/2));
        Check(slot.Into&&slot.Parent==folder.Id,"Folder center did not accept drop into folder.");
        var payload=new LayerDragPayload(d,new[]{a.Id,b.Id});var next=PrepareLayerDrop(payload,slot);
        Check(ReferenceEquals(d,session.Document)&&session.UndoCount==0,"Drop preview mutated the document.");
        ShowLayerDropMark(slot);UpdateLayout();
        Check(layerDropAdorner is not null&&!layerDropAdorner.IsHitTestVisible,"Drop marker missing or intercepting input.");
        var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(path))png.Save(output);
        ClearLayerDropMark();Check(layerDropAdorner is null&&session.UndoCount==0,"Cancelled drop changed document or retained marker.");
        CommitLayerDrop(next,slot);Check(session.Document.Layers.Count(l=>l.ParentId==folder.Id)==2&&Layers.SelectedItems.Count==2,"Folder drop lost roots or selection.");
        bool staleRejected=false;try{PrepareLayerDrop(payload,slot);}catch(InvalidOperationException){staleRejected=true;}Check(staleRejected,"Stale document drag was accepted.");
        session.Undo();Check(ReferenceEquals(d,session.Document)&&Layers.SelectedItems.Count==2,"Drop Undo failed.");UpdateLayout();
        item=(ListBoxItem)Layers.ItemContainerGenerator.ContainerFromItem(Layers.Items.Cast<LayerRow>().Single(r=>r.Id==folder.Id));origin=item.TranslatePoint(new Point(),Layers);
        slot=DropSlot(new Point(origin.X+50,origin.Y+1));Check(!slot.Into&&slot.Above==folder.Id&&slot.Parent is null,"Folder top edge did not reorder above it.");
        next=PrepareLayerDrop(payload,slot);CommitLayerDrop(next,slot);
        Check(session.Document.Layers.Where(l=>l.ParentId is null).Select(l=>l.Id).SequenceEqual(new[]{folder.Id,a.Id,b.Id}),"Row drop reversed selected order.");
        session.Load(d);var copyPayload=new LayerDragPayload(d,new[]{a.Id,b.Id});
        var copySlot=new LayerDropSlot(folder.Id,null,false,new Rect(),true);
        var proposal=PrepareLayerCopyDrop(copyPayload,copySlot);
        Check(ReferenceEquals(proposal,PrepareLayerCopyDrop(copyPayload,copySlot)),"Copy hover recreated the same proposal.");
        Check(ReferenceEquals(d,session.Document)&&session.UndoCount==0,"Copy hover changed source.");
        CommitLayerCopyDrop(proposal,copySlot);
        Check(session.Document.Layers.Length==5&&session.SelectedLayerIds.SetEquals(proposal.Roots)&&Layers.SelectedItems.Count==2,"Copy drop lost copies or selection.");
        Check(session.Document.Layers.Single(l=>l.Id==a.Id).ParentId is null&&proposal.Roots.All(id=>session.Document.Layers.Single(l=>l.Id==id).ParentId==folder.Id),"Copy drop moved its originals.");
        session.Undo();Check(ReferenceEquals(d,session.Document),"Copy drop Undo failed.");session.Redo();Check(session.SelectedLayerIds.SetEquals(proposal.Roots),"Copy drop Redo lost selection.");
        Check(CopyLayerDrag(DragDropKeyStates.ControlKey)&&CopyLayerDrag(DragDropKeyStates.AltKey)&&!CopyLayerDrag(DragDropKeyStates.None),"Copy modifier mapping failed.");
        layerCopyPreview=null;session.Load(d);
    }
}