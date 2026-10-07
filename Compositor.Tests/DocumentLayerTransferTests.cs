using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class DocumentLayerTransferTests
{
    [Fact] public void GroupCopyRemapsHierarchyAndSharesImmutableAssets()
    {
        var source=Document.Create(40,30);var group=Layer.Group("Group",40,30);
        var child=source.Layers[0] with{ParentId=group.Id,Name="Child",Mask=LayerMask.Solid(40,30),Opacity=.4,Visible=false,Transform=new(7,9,40,30,32,true)};
        source=source with{Layers=[group,child]};var destination=Document.Create(100,80);
        var copy=DocumentLayerTransfer.Create(source,destination,new[]{group.Id,child.Id},group.Id);
        Assert.Single(copy.Roots);Assert.Equal(3,copy.Document.Layers.Length);
        var newGroup=copy.Document.Layers.Single(l=>l.Id==copy.Mapping[group.Id]);
        var newChild=copy.Document.Layers.Single(l=>l.Id==copy.Mapping[child.Id]);
        Assert.Null(newGroup.ParentId);Assert.Equal(newGroup.Id,newChild.ParentId);
        Assert.Equal(30,newGroup.Transform.X);Assert.Equal(25,newGroup.Transform.Y);
        Assert.Equal(37,newChild.Transform.X);Assert.Equal(34,newChild.Transform.Y);
        Assert.Equal(32,newChild.Transform.Rotation);Assert.True(newChild.Transform.FlipX);
        Assert.Equal("Child",newChild.Name);Assert.Equal(.4,newChild.Opacity);Assert.False(newChild.Visible);
        Assert.Same(child.Pixels,newChild.Pixels);Assert.Same(child.Mask,newChild.Mask);
        Assert.Single(destination.Layers);Assert.Equal(7,source.Layers[1].Transform.X);
        string path=Path.Combine(Path.GetTempPath(),"compositor-transfer-"+Guid.NewGuid().ToString("N")+".comp");
        try
        {
            ProjectStore.Save(copy.Document,newChild.Id,path);var loaded=ProjectStore.Load(path);
            var savedChild=loaded.Document.Layers.Single(l=>l.Id==newChild.Id);
            Assert.Equal(newChild.Transform,savedChild.Transform);Assert.Equal(newGroup.Id,savedChild.ParentId);
            Assert.Equal(newChild.Mask!.Pixels.ToRgba(),savedChild.Mask!.Pixels.ToRgba());
        }
        finally{if(Directory.Exists(path))Directory.Delete(path,true);}
        var session=new EditorSession(destination);session.Apply(_=>copy.Document);session.SelectLayers(copy.Roots,copy.Roots[0]);
        session.Undo();Assert.Same(destination,session.Document);session.Redo();Assert.Same(copy.Document,session.Document);
    }
    [Fact] public void ChildCopiedAloneDetachesFromExternalParentAndUsesExplicitPoint()
    {
        var source=Document.Create(20,10);var group=Layer.Group("Parent",20,10);
        var child=source.Layers[0] with{ParentId=group.Id};source=source with{Layers=[group,child]};
        var destination=Document.Create(100,100);
        var copy=DocumentLayerTransfer.Create(source,destination,new[]{child.Id},child.Id,new(9,7));
        var layer=copy.Document.Layers.Single(l=>l.Id==copy.Mapping[child.Id]);
        Assert.Null(layer.ParentId);Assert.Equal(-1,layer.Transform.X);Assert.Equal(2,layer.Transform.Y);
        Assert.Equal(child.Name,layer.Name);Assert.NotEqual(child.Id,layer.Id);
    }
    [Fact] public void InvalidRequestsLeaveInputsUnchanged()
    {
        var source=Document.Create(10,10);var destination=Document.Create(20,20);var id=source.Layers[0].Id;
        Assert.Throws<InvalidOperationException>(()=>DocumentLayerTransfer.Create(source,destination,Array.Empty<Guid>(),id));
        Assert.Throws<InvalidOperationException>(()=>DocumentLayerTransfer.Create(source,destination,new[]{id},Guid.NewGuid()));
        Assert.Throws<InvalidDataException>(()=>DocumentLayerTransfer.Create(source,destination,new[]{id},id,new(double.NaN,0)));
        Assert.Throws<InvalidDataException>(()=>DocumentLayerTransfer.Create(source,destination,new[]{id},id,new(2_000_000,0)));
        Assert.Single(source.Layers);Assert.Single(destination.Layers);
    }
    [Fact] public void LayerLimitRejectsTransferBeforeChangingDestination()
    {
        var source=Document.Create(1,1);var destination=Document.Create(1,1);
        destination=destination with{Layers=System.Collections.Immutable.ImmutableArray.CreateRange(Enumerable.Range(0,10000).Select(i=>Layer.Blank("Layer "+i,1,1)))};
        Assert.Throws<InvalidDataException>(()=>DocumentLayerTransfer.Create(source,destination,new[]{source.Layers[0].Id},source.Layers[0].Id));
        Assert.Equal(10000,destination.Layers.Length);Assert.Single(source.Layers);
    }
}