using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class MultiLayerTransformTests
{
    [Fact] public void ParentAndChildSelectionMovesEachImageOnceAndRestoresSelectionInHistory()
    {
        var d=Document.Create(100,100);var a=d.Layers[0];var folder=Layer.Group("Group",100,100);
        d=LayerHierarchy.Wrap(d,a.Id,folder);
        var b=Layer.Blank("Other",10,10) with{Transform=new(110,0,10,10)};
        var hidden=Layer.Blank("Hidden",10,10) with{Visible=false,ParentId=folder.Id};
        d=d with{Layers=d.Layers.Add(b).Add(hidden)};
        var s=new EditorSession(d);s.SelectLayers(new[]{folder.Id,a.Id,b.Id},b.Id);
        using(var edit=LayerTransformEdit.Begin(s))
        {
            Assert.Equal(120,edit.InitialTransform.Width);
            edit.Preview(edit.InitialTransform with{X=20});edit.Complete();
        }
        Assert.Equal(20,s.Document.Layers.First(l=>l.Id==a.Id).Transform.X);
        Assert.Equal(130,s.Document.Layers.First(l=>l.Id==b.Id).Transform.X);
        Assert.Same(hidden,s.Document.Layers.First(l=>l.Id==hidden.Id));
        Assert.Equal(1,s.UndoCount);s.ActiveLayerId=a.Id;s.Undo();
        Assert.Same(d,s.Document);Assert.Equal(b.Id,s.ActiveLayerId);Assert.Equal(3,s.SelectedLayerIds.Count);
        s.Redo();Assert.Single(s.SelectedLayerIds);Assert.Equal(a.Id,s.ActiveLayerId);
    }
    [Fact] public void SelectionChangesAreAtomicAndDoNotDirtyDocument()
    {
        var d=Document.Create(10,10);var b=Layer.Blank("B",10,10);d=d with{Layers=d.Layers.Add(b)};
        var s=new EditorSession(d);s.SelectLayers(d.Layers.Select(l=>l.Id));
        Assert.False(s.IsModified);Assert.Equal(0,s.UndoCount);
        Assert.Throws<ArgumentException>(()=>s.SelectLayers(new[]{Guid.NewGuid()}));Assert.Equal(2,s.SelectedLayerIds.Count);
        Assert.Throws<ArgumentException>(()=>s.SelectLayers(new[]{b.Id},d.Layers[0].Id));Assert.Equal(2,s.SelectedLayerIds.Count);
        s.Begin();Assert.Throws<InvalidOperationException>(()=>s.SelectLayers(new[]{b.Id}));s.Cancel();Assert.Equal(2,s.SelectedLayerIds.Count);
        s.SelectLayers(Array.Empty<Guid>());s.Begin();s.Cancel();Assert.Null(s.ActiveLayerId);Assert.Empty(s.SelectedLayerIds);
        s.Load(d);Assert.Single(s.SelectedLayerIds);
    }
    [Fact] public void DeletingActiveSelectionKeepsSurvivingSelectionAndUndoRestoresBoth()
    {
        var d=Document.Create(10,10);var a=d.Layers[0];var b=Layer.Blank("B",10,10);d=d with{Layers=d.Layers.Add(b)};
        var s=new EditorSession(d);s.SelectLayers(new[]{a.Id,b.Id},b.Id);
        s.Apply(doc=>LayerHierarchy.Delete(doc,b.Id));Assert.Equal(a.Id,s.ActiveLayerId);Assert.Single(s.SelectedLayerIds);
        s.Undo();Assert.Equal(b.Id,s.ActiveLayerId);Assert.Equal(2,s.SelectedLayerIds.Count);
    }
    [Fact] public void MultipleSelectionIgnoresPixelSelectionAndExcludedActiveLayer()
    {
        var d=Document.Create(10,10);var image=d.Layers[0];var adjustment=Layer.ExposureLayer(10,10,null);
        d=d with{Layers=d.Layers.Add(adjustment),Selection=DocumentSelection.Empty};
        var s=new EditorSession(d);s.SelectLayers(new[]{image.Id,adjustment.Id},adjustment.Id);
        using var edit=LayerTransformEdit.Begin(s);Assert.False(edit.IsSelection);
        edit.Preview(edit.InitialTransform with{X=5});
        Assert.Equal(5,s.Document.Layers[0].Transform.X);Assert.Same(adjustment,s.Document.Layers[1]);
        edit.Dispose();Assert.Same(d,s.Document);Assert.Equal(2,s.SelectedLayerIds.Count);
    }
}