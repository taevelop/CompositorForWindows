using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class ImageResizeTests
{
    private static Document Sample()
    {
        var d=Document.Create(2,1);
        return d.Replace(d.Layers[0] with{Pixels=Raster.FromRgba(2,1,[100,20,0,128,0,80,20,128])});
    }
    [Fact] public void ResolutionOnlySharesLayersAndPixelsAndNoOpPreservesSelection()
    {
        var d=Sample() with{Selection=SelectionGeometry.Box(0,0,1,1)};
        Assert.Same(d,ImageResize.Apply(d,new(2,1,72)));
        var next=ImageResize.Apply(d,new(2,1,300));
        Assert.Equal(300,next.Resolution);Assert.Same(d.Layers[0],next.Layers[0]);Assert.Null(next.Selection);
    }
    [Fact] public void NearestScalesPremultipliedPixelsExactlyAndUndoRestoresOriginal()
    {
        var d=Sample();var session=new EditorSession(d);
        session.Apply(doc=>ImageResize.Apply(doc,new(4,2,144,Sampling.Nearest)));
        var next=session.Document;var b=next.Layers[0].Pixels.ToRgba();var original=d.Layers[0].Pixels.ToRgba();
        for(int y=0;y<2;y++)for(int x=0;x<4;x++)
            Assert.Equal(original.AsSpan((x/2)*4,4).ToArray(),b.AsSpan((y*4+x)*4,4).ToArray());
        Assert.Equal(new LayerTransform(0,0,4,2,Sampling:Sampling.Nearest),next.Layers[0].Transform);
        session.Undo();Assert.Same(d,session.Document);session.Redo();Assert.Same(next,session.Document);
    }
    [Fact] public void NonuniformScaleBakesRotationAndFlipWithoutFlatteningLayerMetadata()
    {
        var d=Document.Create(20,20);
        var l=d.Layers[0] with{Pixels=Raster.FromRgba(2,1,[255,0,0,255,0,255,0,255]),Transform=new(4,6,2,1,90,true),
            Effects=new(ColorOverlay:new(.1,.2,.3)),Opacity=.4,Visible=false};
        d=d.Replace(l);var next=ImageResize.Apply(d,new(40,20,72,Sampling.Nearest));var resized=next.Layers[0];
        Assert.Equal(0,resized.Transform.Rotation);Assert.False(resized.Transform.FlipX);
        Assert.Equal(l.Effects,resized.Effects);Assert.Equal(.4,resized.Opacity);Assert.False(resized.Visible);
        Assert.Contains(resized.Pixels.ToRgba().Chunk(4),p=>p[1]>0&&p[0]==0&&p[1]==p[3]);
        Assert.Contains(resized.Pixels.ToRgba().Chunk(4),p=>p[0]==255&&p[3]==255);
    }
    [Theory][InlineData(Sampling.Nearest)][InlineData(Sampling.Smooth)][InlineData(Sampling.High)]
    public void LinkedMaskHasOpaquePaddingAndUniformMaskSharesOriginal(Sampling sampling)
    {
        var d=Sample();var mask=LayerMask.Solid(2,1,100) with{Enabled=false};
        d=d.Replace(d.Layers[0] with{Mask=mask});
        var next=ImageResize.Apply(d,new(5,3,72,sampling));
        Assert.False(next.Layers[0].Mask!.Enabled);Assert.Equal(5,next.Layers[0].Mask!.Pixels.Width);
        _=new LayerMask(next.Layers[0].Mask!.Pixels); // Validate grayscale and padding.
        d=d.Replace(d.Layers[0] with{Mask=LayerMask.Solid(1,1,160)});
        Assert.Same(d.Layers[0].Mask,ImageResize.Apply(d,new(5,3,72,sampling)).Layers[0].Mask);
        var b=next.Layers[0].Pixels.ToRgba();
        for(int i=0;i<b.Length;i+=4)Assert.True(b[i]<=b[i+3]&&b[i+1]<=b[i+3]&&b[i+2]<=b[i+3]);
    }
    [Fact] public void OversizedTransformedLayerAndCancellationLeaveHistoryUntouched()
    {
        var d=Sample();d=d.Replace(d.Layers[0] with{Transform=new(0,0,30000,30000)});
        var session=new EditorSession(d);
        Assert.Throws<InvalidDataException>(()=>session.Apply(doc=>ImageResize.Apply(doc,new(4,2,72))));
        Assert.Same(d,session.Document);Assert.Equal(0,session.UndoCount);
        Assert.Throws<OperationCanceledException>(()=>ImageResize.Apply(Sample(),new(4,2,72),new(true)));
    }
    [Fact] public void GroupAndAdjustmentRemainSeparateAndMasksMatchSourceDimensions()
    {
        var d=Sample();d=LayerHierarchy.Wrap(d,d.Layers[0].Id,Layer.Group("Group",2,1));
        d=d with{Layers=d.Layers.Add(Layer.ExposureLayer(2,1) with{Mask=LayerMask.Solid(2,1,200)})};
        var next=ImageResize.Apply(d,new(4,3,72));
        Assert.Equal(d.Layers.Select(l=>l.Id),next.Layers.Select(l=>l.Id));
        Assert.Equal(d.Layers.Select(l=>l.ParentId),next.Layers.Select(l=>l.ParentId));
        Assert.True(next.Layers[^1].IsAdjustment);Assert.Empty(next.Layers[^1].Pixels.Tiles);
        Assert.Equal(4,next.Layers[^1].Pixels.Width);Assert.Equal(4,next.Layers[^1].Mask!.Pixels.Width);
        next.Validate();
    }
    [Fact] public void SamplingCrossesTileBoundariesAndHighDiffersFromLinear()
    {
        var bytes=new byte[258*4];
        for(int x=0;x<258;x++){bytes[x*4]=(byte)(x%251);bytes[x*4+3]=255;}
        var d=Document.Create(258,1);d=d.Replace(d.Layers[0] with{Pixels=Raster.FromRgba(258,1,bytes)});
        var nearest=ImageResize.Apply(d,new(516,2,72,Sampling.Nearest)).Layers[0].Pixels.ToRgba();
        for(int x=0;x<516;x++)Assert.Equal(bytes[x/2*4],nearest[x*4]);
        var linear=ImageResize.Apply(d,new(517,2,72,Sampling.Smooth)).Layers[0].Pixels.ToRgba();
        var cubic=ImageResize.Apply(d,new(517,2,72,Sampling.High)).Layers[0].Pixels.ToRgba();
        Assert.False(linear.SequenceEqual(cubic));
    }
    [Fact] public void ResizedPixelsMasksAndResolutionSurviveProjectRoundtrip()
    {
        var d=Sample();d=d.Replace(d.Layers[0] with{Mask=LayerMask.Solid(2,1,80)});
        var next=ImageResize.Apply(d,new(5,3,300));
        string root=Path.Combine(Path.GetTempPath(),"compositor-image-size-"+Guid.NewGuid().ToString("N")+".comp");
        try
        {
            ProjectStore.Save(next,next.Layers[0].Id,root);var loaded=ProjectStore.Load(root).Document;
            Assert.Equal(300,loaded.Resolution);Assert.Equal(next.Layers[0].Transform,loaded.Layers[0].Transform);
            Assert.Equal(next.Layers[0].Pixels.ToRgba(),loaded.Layers[0].Pixels.ToRgba());
            Assert.Equal(next.Layers[0].Mask!.Pixels.ToRgba(),loaded.Layers[0].Mask!.Pixels.ToRgba());
        }
        finally
        {
            string full=Path.GetFullPath(root),temp=Path.GetFullPath(Path.GetTempPath());
            if(full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith("compositor-image-size-")&&Directory.Exists(full))Directory.Delete(full,true);
        }
    }
}
