using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class SelectionTransformPixelsTests
{
    private static Document Sample(bool mask=false)
    {
        var d=Document.Create(4,4);var bytes=new byte[64];
        bytes[(1*4+1)*4]=255;bytes[(1*4+1)*4+3]=255;
        bytes[(1*4+2)*4+1]=255;bytes[(1*4+2)*4+3]=255;
        var layer=d.Layers[0] with{Pixels=Raster.FromRgba(4,4,bytes),Mask=mask?LayerMask.Solid(1,1,128):null,Effects=new(ColorOverlay:new(.2,.4,.5)),Opacity=.7};
        return d.Replace(layer) with{Selection=SelectionGeometry.Box(1,1,2,1,false,false)};
    }
    [Fact] public void UnchangedTransformRestoresExactOriginalEvenAfterPreview()
    {
        var d=Sample() with{Selection=SelectionGeometry.Box(1,1,2,1) with{Feather=2}};
        var edit=SelectionTransformPixels.Create(d,d.Layers[0].Id)!;
        _=edit.Apply(edit.InitialTransform with{X=5});Assert.Same(d,edit.Apply(edit.InitialTransform));
        var s=new EditorSession(d);s.Begin();s.Preview(edit.Apply(edit.InitialTransform with{X=5}));s.Cancel();
        Assert.Same(d,s.Document);Assert.Equal(0,s.UndoCount);
    }
    [Fact] public void TranslationExpandsSourceAndMaskWhiteWithoutChangingEffects()
    {
        var d=Sample(true);var edit=SelectionTransformPixels.Create(d,d.Layers[0].Id)!;
        var next=edit.Apply(edit.InitialTransform with{X=3,Sampling=Sampling.Nearest});var l=next.Layers[0];var b=l.Pixels.ToRgba();
        Assert.Equal(5,l.Pixels.Width);Assert.Equal(0,b[(1*5+1)*4+3]);
        Assert.Equal(255,b[(1*5+3)*4]);Assert.Equal(255,b[(1*5+4)*4+1]);
        Assert.Equal(128,l.Mask!.Pixels.ToRgba()[(1*5+3)*4]);Assert.Equal(255,l.Mask.Pixels.ToRgba()[(1*5+4)*4]);
        Assert.Same(d.Layers[0].Effects,l.Effects);Assert.Equal(.7,l.Opacity);
        Assert.True(SelectionGeometry.Contains(next.Selection!,new(3.5,1.5)));Assert.False(SelectionGeometry.Contains(next.Selection!,new(1.5,1.5)));
    }
    [Fact] public void FlipAndScaleUseOriginalLiftedPixelsForEveryPreview()
    {
        var d=Sample();var edit=SelectionTransformPixels.Create(d,d.Layers[0].Id)!;
        var flipped=edit.Apply(edit.InitialTransform with{FlipX=true,Sampling=Sampling.Nearest});var b=flipped.Layers[0].Pixels.ToRgba();
        Assert.Equal(255,b[(1*4+1)*4+1]);Assert.Equal(255,b[(1*4+2)*4]);
        var scaled=edit.Apply(edit.InitialTransform with{Width=4,Sampling=Sampling.Nearest});b=scaled.Layers[0].Pixels.ToRgba();
        Assert.Equal(5,scaled.Layers[0].Pixels.Width);Assert.Equal(255,b[(1*5+2)*4]);Assert.Equal(255,b[(1*5+3)*4+1]);
        Assert.Equal(scaled.Layers[0].Pixels.ToRgba(),edit.Apply(edit.InitialTransform with{Width=4,Sampling=Sampling.Nearest}).Layers[0].Pixels.ToRgba());
    }
    [Fact] public void RotationMovesOutlineAndKeepsPremultipliedPixels()
    {
        var d=Sample();var edit=SelectionTransformPixels.Create(d,d.Layers[0].Id)!;
        var next=edit.Apply(edit.InitialTransform with{Rotation=90});
        using var path=SelectionGeometry.Path(next.Selection!);
        Assert.Equal(1,path.Bounds.Width,3);Assert.Equal(2,path.Bounds.Height,3);
        var b=next.Layers[0].Pixels.ToRgba();for(int i=0;i<b.Length;i+=4)Assert.True(b[i]<=b[i+3]&&b[i+1]<=b[i+3]&&b[i+2]<=b[i+3]);
    }
    [Fact] public void InvalidTargetsCancellationAndOversizeDoNotChangeOriginal()
    {
        var d=Sample();Assert.Throws<InvalidOperationException>(()=>SelectionTransformPixels.Create(d,d.Layers[0].Id,true));
        Assert.Null(SelectionTransformPixels.Create(d with{Selection=DocumentSelection.Empty},d.Layers[0].Id));
        var edit=SelectionTransformPixels.Create(d,d.Layers[0].Id)!;
        Assert.Throws<InvalidDataException>(()=>edit.Apply(edit.InitialTransform with{Width=300000}));
        Assert.Throws<OperationCanceledException>(()=>edit.Apply(edit.InitialTransform with{X=3},new(true)));
        Assert.Equal(255,d.Layers[0].Pixels.ToRgba()[(1*4+1)*4]);
    }
    [Fact] public void RotatedSourceKeepsPlacementMetadataAndRoundtripsAfterOneUndo()
    {
        var d=Sample(true) with{Width=20,Height=20,Selection=SelectionGeometry.Box(0,0,20,20) with{Feather=1}};
        d=d.Replace(d.Layers[0] with{Transform=new(3,4,8,6,30,true)});
        d=LayerHierarchy.Wrap(d,d.Layers[0].Id,Layer.Group("Group",20,20));
        var source=d.Layers.First(l=>!l.IsGroup);var session=new EditorSession(d){ActiveLayerId=source.Id};
        var edit=SelectionTransformPixels.Create(d,source.Id)!;
        var next=edit.Apply(edit.InitialTransform with{X=2,Rotation=15,Width=24});
        var layer=next.Layers.First(l=>l.Id==source.Id);
        Assert.Equal(30,layer.Transform.Rotation);Assert.True(layer.Transform.FlipX);Assert.Equal(source.ParentId,layer.ParentId);
        Assert.Equal(layer.Pixels.Width,layer.Mask!.Pixels.Width);Assert.Equal(1,next.Selection!.Feather);
        session.Begin();session.Preview(next);session.Commit();Assert.Equal(1,session.UndoCount);
        session.Undo();Assert.Same(d,session.Document);session.Redo();Assert.Same(next,session.Document);
        string root=Path.Combine(Path.GetTempPath(),"compositor-selection-transform-"+Guid.NewGuid().ToString("N")+".comp");
        try
        {
            ProjectStore.Save(next,source.Id,root);var loaded=ProjectStore.Load(root);
            var saved=loaded.Document.Layers.First(l=>l.Id==source.Id);
            Assert.Equal(source.Id,loaded.ActiveLayerId);Assert.Equal(layer.Transform,saved.Transform);
            Assert.Equal(layer.Pixels.ToRgba(),saved.Pixels.ToRgba());Assert.Equal(layer.Mask.Pixels.ToRgba(),saved.Mask!.Pixels.ToRgba());
            Assert.Equal(layer.Effects,saved.Effects);
        }
        finally
        {
            string full=Path.GetFullPath(root),temp=Path.GetFullPath(Path.GetTempPath());
            if(full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith("compositor-selection-transform-")&&Directory.Exists(full))Directory.Delete(full,true);
        }
    }
}
