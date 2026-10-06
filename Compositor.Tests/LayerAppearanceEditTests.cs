using Compositor.Core;
using Xunit;
namespace Compositor.Tests;
public sealed class LayerAppearanceEditTests
{
    [Fact] public void OnlyCheckedPropertiesChangeAndHistoryPreservesSelection()
    {
        var d=Document.Create(3,3);var a=d.Layers[0] with{Opacity=.2,Blend=BlendMode.Multiply};var b=Layer.Blank("B",3,3) with{Opacity=.8,Visible=false};
        var other=Layer.Blank("Other",3,3);d=d with{Layers=[a,b,other]};var s=new EditorSession(d);s.SelectLayers(new[]{a.Id,b.Id});
        s.Apply(doc=>LayerAppearanceEdit.Apply(doc,s.SelectedLayerIds,opacity:.5));
        Assert.Equal(.5,s.Document.Layers[0].Opacity);Assert.Equal(.5,s.Document.Layers[1].Opacity);Assert.False(s.Document.Layers[1].Visible);
        Assert.Equal(BlendMode.Multiply,s.Document.Layers[0].Blend);Assert.Same(a.Pixels,s.Document.Layers[0].Pixels);Assert.Same(other,s.Document.Layers[2]);
        var next=s.Document;s.Undo();Assert.Same(d,s.Document);Assert.Equal(2,s.SelectedLayerIds.Count);s.Redo();Assert.Same(next,s.Document);
    }
    [Fact] public void SelectedFolderAndChildBothReceiveOpacityLikeOriginal()
    {
        var d=Document.Create(2,2);var a=d.Layers[0];var group=Layer.Group("G",2,2);d=LayerHierarchy.Wrap(d,a.Id,group);
        var next=LayerAppearanceEdit.Apply(d,new[]{a.Id,group.Id},opacity:.5,visible:false);
        Assert.All(next.Layers,l=>{Assert.Equal(.5,l.Opacity);Assert.False(l.Visible);});
        Assert.Throws<InvalidOperationException>(()=>LayerAppearanceEdit.Apply(d,new[]{a.Id,group.Id},blend:BlendMode.Multiply));
    }
    [Fact] public void InvalidAndNoOpPatchesPreserveDocument()
    {
        var d=Document.Create(2,2);var ids=new[]{d.Layers[0].Id};
        Assert.Same(d,LayerAppearanceEdit.Apply(d,ids));Assert.Same(d,LayerAppearanceEdit.Apply(d,ids,opacity:1));
        Assert.Throws<InvalidDataException>(()=>LayerAppearanceEdit.Apply(d,ids,opacity:double.NaN));
        Assert.Throws<InvalidDataException>(()=>LayerAppearanceEdit.Apply(d,ids,opacity:1.01));
        Assert.Throws<InvalidDataException>(()=>LayerAppearanceEdit.Apply(d,ids,blend:(BlendMode)999));
        Assert.Throws<InvalidOperationException>(()=>LayerAppearanceEdit.Apply(d,new[]{Guid.NewGuid()},visible:false));
    }
}