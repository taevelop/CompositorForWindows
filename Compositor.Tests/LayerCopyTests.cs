using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class LayerCopyTests
{
    [Fact] public void DuplicateSharesPixelsMaskEffectsAndRestoresActiveLayerOnUndo()
    {
        var d=Document.Create(10,10);d=d.Replace(d.Layers[0] with{Pixels=LayerMask.Solid(10,10,100).Pixels,Mask=LayerMask.Solid(1,1,120),Effects=new(ColorOverlay:new(.3,.4,.5))});
        var s=new EditorSession(d);var id=LayerCopy.ViaSelection(s);var clone=s.ActiveLayer!;
        Assert.Equal(id,clone.Id);Assert.Equal("Layer 1 copy",clone.Name);
        Assert.Same(d.Layers[0].Pixels,clone.Pixels);Assert.Same(d.Layers[0].Mask,clone.Mask);Assert.Equal(d.Layers[0].Effects,clone.Effects);
        Assert.Equal(0,s.HistoryRetainedBytes);var next=s.Document;s.Undo();Assert.Same(d,s.Document);Assert.Equal(d.Layers[0].Id,s.ActiveLayerId);s.Redo();Assert.Same(next,s.Document);
    }
    [Fact] public void GroupCopyRemapsAllDescendantsButKeepsExternalParent()
    {
        var d=Document.Create(8,8);var nested=Layer.Group("Nested",8,8);d=LayerHierarchy.Wrap(d,d.Layers[0].Id,nested);
        var outer=Layer.Group("Outer",8,8);d=LayerHierarchy.Wrap(d,nested.Id,outer);
        var s=new EditorSession(d){ActiveLayerId=nested.Id};var id=LayerCopy.Duplicate(s)!.Value;
        Assert.Equal(5,s.Document.Layers.Length);Assert.Equal(outer.Id,s.ActiveLayer!.ParentId);
        var children=s.Document.Layers.Where(l=>l.ParentId==id).ToArray();Assert.Single(children);
        Assert.NotEqual(d.Layers.First(l=>!l.IsGroup).Id,children[0].Id);s.Document.Validate();
    }
    [Fact] public void SelectionCopyMakesRawPixelsInPlaceWithoutTouchingSource()
    {
        var d=Document.Create(8,8);var layer=d.Layers[0] with{Pixels=LayerMask.Solid(8,8,100).Pixels,Opacity=.2,Mask=LayerMask.Solid(1,1,0)};
        d=d.Replace(layer) with{Selection=SelectionGeometry.Box(2,3,3,2,false,false)};
        var s=new EditorSession(d);LayerCopy.ViaSelection(s);var copied=s.ActiveLayer!;
        Assert.Equal(new LayerTransform(2,3,3,2),copied.Transform);Assert.Equal(1,copied.Opacity);Assert.Null(copied.Mask);
        Assert.Equal(255,copied.Pixels.ToRgba()[3]);Assert.Null(s.Document.Selection);Assert.Same(layer,s.Document.Layers[0]);Assert.Equal(1,s.UndoCount);
        s.Undo();Assert.Same(d,s.Document);
    }
    [Fact] public void MaskCopyEmptySelectionAndSourceBudgetAreHandled()
    {
        var d=Document.Create(4,4);d=d.Replace(d.Layers[0] with{Mask=LayerMask.Solid(1,1,80)}) with{Selection=SelectionGeometry.Box(0,0,2,2)};
        var s=new EditorSession(d){EditMask=true};LayerCopy.ViaSelection(s);Assert.Equal(80,s.ActiveLayer!.Pixels.ToRgba()[0]);Assert.False(s.EditMask);
        s.Load(d with{Selection=DocumentSelection.Empty});Assert.Null(LayerCopy.ViaSelection(s));Assert.Equal(0,s.UndoCount);
        var huge=Layer.Blank("Huge",8000,8000) with{Mask=LayerMask.Solid(1,1,255)};
        s.Load(d with{Layers=[huge],Selection=null});var before=s.Document;
        Assert.Throws<InvalidDataException>(()=>LayerCopy.Duplicate(s));Assert.Same(before,s.Document);Assert.Equal(0,s.UndoCount);
    }
}
