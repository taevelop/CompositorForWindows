using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class ShapeDragTests
{
    [Fact] public void RectangleRoundsAndConstrainsAndCenteredDragMatchesOriginal()
    {
        var drag=new ShapeDrag(new(10.5,20.5),new(ShapeKind.Rectangle,1,0,0,3));
        Assert.Equal(new PointD(11,21),drag.Anchor);Assert.Null(drag.Update(drag.Anchor));
        Assert.Equal(new LayerTransform(11,21,9,4),drag.Update(new(20,25))!.Transform);
        Assert.Equal(new LayerTransform(2,12,18,18),drag.Update(new(20,25),true,true)!.Transform);
        Assert.Equal(new LayerTransform(2,12,9,9),drag.Update(new(2,17),true)!.Transform);
    }
    [Fact] public void LinePreservesExactEndpointsAndShiftAngleAndOriginalCenterBehavior()
    {
        var drag=new ShapeDrag(new(10,10),new(ShapeKind.Line,0,0,1,LineWidth:4));
        var draft=drag.Update(new(30,15))!;Assert.Equal(new LayerTransform(8,8,24,9),draft.Transform);
        var start=draft.Transform.ToDocument(draft.Style.Start!.Value,1,1);var end=draft.Transform.ToDocument(draft.Style.End!.Value,1,1);
        Assert.Equal(new PointD(10,10),start);Assert.Equal(new PointD(30,15),end);
        Assert.Equal(draft,drag.Update(new(30,15),centered:true));
        var snapped=drag.Update(new(30,15),true)!;Assert.Equal(4,snapped.Transform.Height);
    }
    [Fact] public void InsertKeepsSelectionAndGroupParentAndOneUndoAndRejectsCancelledWork()
    {
        var d=Document.Create(32,32);var group=Layer.Group("Group",32,32);d=d with{Layers=d.Layers.Add(group),Selection=SelectionGeometry.Box(0,0,2,2)};
        var session=new EditorSession(d);var draft=new ShapeDrag(new(4,4),new(ShapeKind.Ellipse,1,0,0)).Update(new(12,12))!;
        var id=ShapeInsert.Add(session,draft);Assert.Equal(id,session.ActiveLayerId);Assert.Equal(group.Id,session.ActiveLayer!.ParentId);
        Assert.Equal("Ellipse 1",session.ActiveLayer.Name);Assert.Same(d.Selection,session.Document.Selection);
        session.Undo();Assert.Same(d,session.Document);Assert.False(session.CanUndo);session.Redo();Assert.Equal(id,session.ActiveLayerId);
        using var cancel=new CancellationTokenSource();cancel.Cancel();var before=session.Document;
        Assert.Throws<OperationCanceledException>(()=>ShapeInsert.Add(session,draft,cancel.Token));Assert.Same(before,session.Document);
    }
}
