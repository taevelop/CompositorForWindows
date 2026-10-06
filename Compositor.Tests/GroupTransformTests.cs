using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class GroupTransformTests
{
    private static (Document Document,Guid Group,Guid Image) Sample()
    {
        var d=Document.Create(100,100);var image=d.Layers[0] with{Transform=new(10,20,30,40),Mask=LayerMask.Solid(1,1,120),Effects=new(ColorOverlay:new(.2,.3,.4))};
        d=d.Replace(image);var group=Layer.Group("Group",100,100);d=LayerHierarchy.Wrap(d,image.Id,group);
        d=d with{Layers=d.Layers.Add(Layer.Blank("Hidden",5,5) with{Visible=false,ParentId=group.Id,Transform=new(-500,-500,5,5)}).Add(Layer.ExposureLayer(100,100,group.Id))};
        return(d,group.Id,image.Id);
    }
    [Fact] public void BoundsAndMovementOnlyIncludeVisibleImageDescendants()
    {
        var(d,root,id)=Sample();var edit=new GroupTransform(d,root);Assert.Equal(new LayerTransform(10,20,30,40),edit.Bounds);
        var next=edit.Apply(edit.Bounds with{X=25,Y=30});
        foreach(var layer in d.Layers)
        {
            var moved=next.Layers.First(l=>l.Id==layer.Id);
            if(layer.Id==id){Assert.Equal(25,moved.Transform.X);Assert.Equal(30,moved.Transform.Y);Assert.Same(layer.Pixels,moved.Pixels);Assert.Same(layer.Mask,moved.Mask);Assert.Same(layer.Effects,moved.Effects);}
            else Assert.Same(layer,moved);
        }
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void UniformScaleRotationAndReflectionCarryAllCorners(bool flip)
    {
        var source=new LayerTransform(10,20,30,15,25,true,false,Sampling.Nearest);
        var from=new LayerTransform(0,0,100,100);var to=new LayerTransform(50,60,200,200,35,flip);
        var next=GroupTransform.Following(source,from,to);
        foreach(var p in new[]{new PointD(0,0),new PointD(1,0),new PointD(1,1),new PointD(0,1)})
        {
            var expected=to.ToDocument(from.ToPixels(source.ToDocument(p,1,1),1,1),1,1);
            var actual=next.ToDocument(p,1,1);Assert.Equal(expected.X,actual.X,7);Assert.Equal(expected.Y,actual.Y,7);
        }
        Assert.Equal(source.Sampling,next.Sampling);
    }
    [Fact] public void NonuniformRotatedScaleMatchesOriginalShearDiscardRule()
    {
        var source=new LayerTransform(0,0,10,10,45);var from=new LayerTransform(0,0,100,100);var to=new LayerTransform(0,0,200,100);
        var next=GroupTransform.Following(source,from,to);
        Assert.Equal(Math.Sqrt(250),next.Width,7);Assert.Equal(200/Math.Sqrt(250),next.Height,7);
        Assert.Equal(Math.Atan(.5)*180/Math.PI,next.Rotation,7);
    }
    [Fact] public void GroupControllerCommitsOneUndoAndCancelRestoresOriginal()
    {
        var(d,root,id)=Sample();var session=new EditorSession(d){ActiveLayerId=root};
        using(var edit=LayerTransformEdit.Begin(session)){edit.Preview(edit.InitialTransform with{Rotation=30});}
        Assert.Same(d,session.Document);Assert.False(session.InTransaction);
        using(var edit=LayerTransformEdit.Begin(session)){edit.Preview(edit.InitialTransform with{Width=60,Height=80});edit.Complete();}
        Assert.Equal(1,session.UndoCount);Assert.Equal(60,session.Document.Layers.First(l=>l.Id==id).Transform.Width,7);
        var next=session.Document;session.Undo();Assert.Same(d,session.Document);session.Redo();Assert.Same(next,session.Document);
    }
    [Fact] public void EmptyGroupAndInvalidChildSizeCannotLeavePartialEdit()
    {
        var empty=Document.Create(10,10) with{Layers=[Layer.Group("Empty",10,10)]};var s=new EditorSession(empty);
        Assert.Throws<InvalidOperationException>(()=>LayerTransformEdit.Begin(s));Assert.False(s.InTransaction);
        var(d,root,id)=Sample();d=d with{Layers=d.Layers.Add(Layer.Blank("Small",1,1) with{ParentId=root,Transform=new(99,99,1,1)})};
        s.Load(d,root);using var edit=LayerTransformEdit.Begin(s);
        Assert.Throws<InvalidDataException>(()=>edit.Preview(edit.InitialTransform with{Width=1,Height=1}));
        Assert.Same(d,s.Document);Assert.False(s.InTransaction);
    }
    [Fact] public void GroupResultRoundtripsWithMaskEffectsAndActiveFolder()
    {
        var(d,root,id)=Sample();var edit=new GroupTransform(d,root);
        var next=edit.Apply(edit.Bounds with{Rotation=25,Width=60,Height=80});
        string path=Path.Combine(Path.GetTempPath(),"compositor-group-transform-"+Guid.NewGuid().ToString("N")+".comp");
        try
        {
            ProjectStore.Save(next,root,path);var loaded=ProjectStore.Load(path);
            Assert.Equal(root,loaded.ActiveLayerId);
            foreach(var layer in next.Layers)
            {
                var copy=loaded.Document.Layers.First(l=>l.Id==layer.Id);
                Assert.Equal(layer.Transform,copy.Transform);Assert.Equal(layer.ParentId,copy.ParentId);
                Assert.Equal(layer.Effects,copy.Effects);Assert.Equal(layer.Pixels.ToRgba(),copy.Pixels.ToRgba());
                if(layer.Mask is {} mask)Assert.Equal(mask.Pixels.ToRgba(),copy.Mask!.Pixels.ToRgba());
            }
        }
        finally
        {
            string full=Path.GetFullPath(path),temp=Path.GetFullPath(Path.GetTempPath());
            if(full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith("compositor-group-transform-")&&Directory.Exists(full))Directory.Delete(full,true);
        }
    }
}
