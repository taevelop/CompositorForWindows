using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class TransformDragTests
{
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)][InlineData(6)][InlineData(7)]
    public void RotatedHandlesHaveNoGrabJumpAndPreserveOppositeAnchor(int index)
    {
        var t=new LayerTransform(10,20,100,60,37,true);
        var h=CropGeometry.Handles[index];var initial=TransformDrag.Point(t,h);var start=new PointD(initial.X+2,initial.Y-3);
        var drag=new TransformDrag(t,start,TransformDragMode.Resize,index);var same=drag.Update(start);Assert.Same(t,same);
        Assert.Equal(t.Width,same.Width,8);Assert.Equal(t.Height,same.Height,8);Assert.Equal(t.X,same.X,8);Assert.Equal(t.Y,same.Y,8);
        var next=drag.Update(new(start.X+7,start.Y+5));var anchor=new PointD(1-h.X,1-h.Y);
        var oldPoint=TransformDrag.Point(t,anchor);var newPoint=TransformDrag.Point(next,anchor);
        Assert.Equal(oldPoint.X,newPoint.X,8);Assert.Equal(oldPoint.Y,newPoint.Y,8);
        next=drag.Update(new(start.X+7,start.Y+5),fromCenter:true);
        Assert.Equal(t.X+t.Width/2,next.X+next.Width/2,8);Assert.Equal(t.Y+t.Height/2,next.Y+next.Height/2,8);
    }
    [Fact] public void CrossingEdgeFlipsAndShiftTogglesRatio()
    {
        var t=new LayerTransform(0,0,100,50);
        var flip=new TransformDrag(t,new(0,25),TransformDragMode.Resize,7).Update(new(120,25));
        Assert.True(flip.FlipX);Assert.Equal(20,flip.Width);Assert.Equal(100,flip.X);
        var drag=new TransformDrag(t,new(100,50),TransformDragMode.Resize,4);
        var locked=drag.Update(new(130,80),true);Assert.Equal(2,locked.Width/locked.Height,8);
        var free=drag.Update(new(130,80),true,true);Assert.Equal(130,free.Width);Assert.Equal(80,free.Height);
    }
    [Fact] public void MoveAxisConstraintRotateSnapAndInvalidBounds()
    {
        var t=new LayerTransform(0,0,100,50);
        Assert.Equal(t with{X=20},new TransformDrag(t,new(0,0),TransformDragMode.Move).Update(new(20,5),shift:true));
        var rotation=new TransformDrag(t,new(100,25),TransformDragMode.Rotate).Update(new(50,75),shift:true);
        Assert.Equal(90,rotation.Rotation,8);
        Assert.Equal(t,new TransformDrag(t,new(0,0),TransformDragMode.Move).Update(new(2_000_000,0)));
        Assert.True(TransformDrag.Contains(t with{FlipX=true},new(20,20)));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void EditUsesOneUndoAndCancelOrReturnToOriginRestoresExactOriginal(bool selection)
    {
        var d=Document.Create(8,8);d=d.Replace(d.Layers[0] with{Pixels=LayerMask.Solid(8,8,100).Pixels});
        if(selection)d=d with{Selection=SelectionGeometry.Box(2,2,2,2)};
        var session=new EditorSession(d);
        using(var edit=LayerTransformEdit.Begin(session)){edit.Preview(edit.InitialTransform with{X=4});edit.Preview(edit.InitialTransform);edit.Complete();}
        Assert.Same(d,session.Document);Assert.Equal(0,session.UndoCount);
        using(var edit=LayerTransformEdit.Begin(session)){edit.Preview(edit.InitialTransform with{X=4});}
        Assert.Same(d,session.Document);Assert.False(session.InTransaction);
        using(var edit=LayerTransformEdit.Begin(session)){edit.Preview(edit.InitialTransform with{Rotation=30});edit.Complete();}
        var next=session.Document;Assert.Equal(1,session.UndoCount);session.Undo();Assert.Same(d,session.Document);session.Redo();Assert.Same(next,session.Document);
    }
    [Fact] public void InvalidPreviewCancelsAndReleasesTransaction()
    {
        var d=Document.Create(8,8);var session=new EditorSession(d);
        using var edit=LayerTransformEdit.Begin(session);
        Assert.Throws<InvalidDataException>(()=>edit.Preview(edit.InitialTransform with{Width=0}));
        Assert.Same(d,session.Document);Assert.False(session.InTransaction);
    }
    [Fact] public void OutsideSelectionNeverFallsBackToTransformingWholeLayer()
    {
        var d=Document.Create(8,8) with{Selection=SelectionGeometry.Box(100,100,2,2)};
        var session=new EditorSession(d);
        Assert.Throws<InvalidOperationException>(()=>LayerTransformEdit.Begin(session));
        Assert.Same(d,session.Document);Assert.False(session.InTransaction);
    }
}
