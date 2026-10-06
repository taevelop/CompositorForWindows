using System.Windows;
using System.Windows.Controls;
using Compositor.Core;
namespace Compositor.App;
public partial class MainWindow
{
    private void LayerDragAssistSmokeTest()
    {
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        var initial=session.Document;var active=session.ActiveLayerId;
        try
        {
            var d=Document.Create(1,1);var source=d.Layers[0];var folder=Layer.Group("Hover",1,1);var child=Layer.Blank("Inside",1,1) with{ParentId=folder.Id};
            d=d with{Layers=d.Layers.Add(folder).Add(child)};session.Load(d,source.Id);collapsedGroups.Add(folder.Id);Refresh();UpdateLayout();
            var item=(ListBoxItem)Layers.ItemContainerGenerator.ContainerFromItem(Layers.Items.Cast<LayerRow>().Single(r=>r.Id==folder.Id));
            var origin=item.TranslatePoint(new Point(),Layers);var point=new Point(origin.X+40,origin.Y+item.ActualHeight/2);
            layerDragPayload=new(d,new[]{source.Id});TrackLayerDrag(point,false,1000);RefreshLayerDragTarget(1000);
            TickLayerDragAssist(1649);Check(collapsedGroups.Contains(folder.Id),"Folder opened before hover delay.");
            TickLayerDragAssist(1650);Check(!collapsedGroups.Contains(folder.Id)&&Layers.Items.Cast<LayerRow>().Any(r=>r.Id==child.Id),"Stationary hover did not expand folder.");
            Check(ReferenceEquals(d,session.Document)&&session.UndoCount==0,"Hover expansion edited document.");StopLayerDragAssist();ClearLayerDropMark();
            var many=Document.Create(1,1) with{Layers=[..Enumerable.Range(0,60).Select(i=>Layer.Blank("Row "+i,1,1))]};
            session.Load(many);UpdateLayout();var scroll=LayerDragVisual<ScrollViewer>(Layers)!;scroll.ScrollToTop();UpdateLayout();
            layerDragPayload=new(many,new[]{many.Layers[^1].Id});point=new(40,Layers.ActualHeight-3);
            TrackLayerDrag(point,false,2000);RefreshLayerDragTarget(2000);double before=scroll.VerticalOffset;
            TickLayerDragAssist(2100);Check(scroll.VerticalOffset>before,"Bottom edge did not scroll stationary drag.");
            double after=scroll.VerticalOffset;TickLayerDragAssist(2150);Check(scroll.VerticalOffset==after,"Scroll ignored repeat interval.");
            TrackLayerDrag(new(40,3),false,2200);TickLayerDragAssist(2200);Check(scroll.VerticalOffset<after,"Top edge did not scroll upward.");
            TrackLayerDrag(new(-1,20),false,2300);TickLayerDragAssist(2300);
            Check(layerDragAssistTimer?.IsEnabled==false&&layerDragAssistPoint is null&&layerDropAdorner is null,"Leaving viewport did not stop assist.");
            TrackLayerDrag(point,true,2400);session.Load(Document.Create(2,2));TickLayerDragAssist(2500);
            Check(layerDragAssistTimer?.IsEnabled==false,"Stale document did not stop assist.");
        }
        finally{StopLayerDragAssist();ClearLayerDropMark();layerDragPayload=null;layerCopyPreview=null;session.Load(initial,active);}
    }
}