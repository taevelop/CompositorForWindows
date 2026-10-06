using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class CanvasResizeTests
{
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)][InlineData(6)][InlineData(7)][InlineData(8)]
    public void EveryAnchorKeepsItsPointAndFloorsOddOffsets(int anchor)
    {
        var d=Document.Create(10,8);
        foreach(var size in new[]{(13,11),(7,5)})
        {
            var options=new CanvasSizeOptions(size.Item1,size.Item2,anchor);
            var offset=options.Offset(d.Width,d.Height);
            Assert.Equal(Math.Floor((size.Item1-10)*(anchor%3)/2d),offset.X);
            Assert.Equal(Math.Floor((size.Item2-8)*(anchor/3)/2d),offset.Y);
            var next=CanvasResize.Apply(d,options);
            Assert.Equal(offset.X,next.Layers[0].Transform.X);Assert.Equal(offset.Y,next.Layers[0].Transform.Y);
            Assert.Same(d.Layers[0].Pixels,next.Layers[0].Pixels);
        }
    }
    [Fact] public void FillOnlyTouchesExtensionAndPreservesTransparentHoles()
    {
        var d=Document.Create(3,3) with{Selection=SelectionGeometry.Box(0,0,2,2)};
        var next=CanvasResize.Apply(d,new(6,4,4,new(30,80,120)));
        Assert.Equal(2,next.Layers.Length);Assert.Null(next.Selection);
        var fill=next.Layers[0];Assert.Equal("Canvas Extension",fill.Name);Assert.Null(fill.ParentId);
        var bytes=fill.Pixels.ToRgba();
        for(int y=0;y<4;y++)for(int x=0;x<6;x++)
        {
            bool old=x>=1&&x<4&&y<3;int i=(y*6+x)*4;
            Assert.Equal(old?0:255,bytes[i+3]);Assert.Equal(old?0:30,bytes[i]);
        }
        Assert.Same(d.Layers[0].Pixels,next.Layers[1].Pixels);
        Assert.Single(CanvasResize.Apply(d,new(2,2,4,new(1,2,3))).Layers);
        Assert.Same(d,CanvasResize.Apply(d,new(3,3,4,new(1,2,3))));
    }
    [Fact] public void MixedShrinkAndExpansionKeepsIntersectionAcrossTileBoundaries()
    {
        var d=Document.Create(260,260);var next=CanvasResize.Apply(d,new(258,263,8,new(1,2,3)));
        var b=next.Layers[0].Pixels.ToRgba();
        Assert.Equal(255,b[(2*258+257)*4+3]);Assert.Equal(0,b[(3*258+257)*4+3]);
        Assert.Equal(0,b[(262*258+257)*4+3]);
        Assert.Equal(-2,next.Layers[1].Transform.X);Assert.Equal(3,next.Layers[1].Transform.Y);
    }
    [Fact] public void BudgetCancellationAndInvalidAnchorDoNotChangeHistory()
    {
        var d=Document.Create(10,10);var session=new EditorSession(d);
        Assert.Throws<ArgumentOutOfRangeException>(()=>session.Apply(doc=>CanvasResize.Apply(doc,new(11,11,9))));
        Assert.Throws<OperationCanceledException>(()=>session.Apply(doc=>CanvasResize.Apply(doc,new(11,11),new(true))));
        var large=Layer.Blank("Large",10000,10000) with{Mask=LayerMask.Solid(1,1,255)};
        var big=d with{Layers=[large]};
        Assert.Throws<InvalidDataException>(()=>CanvasResize.Apply(big,new(11,11,4,new(1,2,3))));
        Assert.Same(d,session.Document);Assert.Equal(0,session.UndoCount);
    }
    [Fact] public void UnitsRelativeLockAndRoundingMatchOriginalDraft()
    {
        var draft=new CanvasSizeDraft(200,100,100){Unit=CanvasUnit.Inches};
        Assert.Equal(2,draft.Displayed(true));draft.Set(2.54,false);
        Assert.Equal(254,draft.Height);
        draft.Unit=CanvasUnit.Centimeters;draft.Set(2.54,true);Assert.Equal(100,draft.Width);
        draft.Unit=CanvasUnit.Percent;draft.Relative=true;draft.Locked=true;
        draft.Set(25,true);Assert.Equal((250,125),draft.Dimensions());Assert.Equal(25,draft.Displayed(false));
        draft.Unit=CanvasUnit.Pixels;draft.Relative=false;draft.Locked=false;draft.Set(10.5,true);
        Assert.Equal(11,draft.Dimensions().Width);
        draft.Set(-2,true);Assert.Throws<InvalidDataException>(()=>draft.Dimensions());
    }
    [Fact] public void ExtensionAndOriginalMaskSurviveSaveReopen()
    {
        var d=Document.Create(10,8);d=d.Replace(d.Layers[0] with{Mask=LayerMask.Solid(1,1,120),Effects=new(ColorOverlay:new(.4,.2,.1))});
        var next=CanvasResize.Apply(d,new(15,12,4,new(20,40,80)));
        string root=Path.Combine(Path.GetTempPath(),"compositor-canvas-size-"+Guid.NewGuid().ToString("N")+".comp");
        try
        {
            ProjectStore.Save(next,d.Layers[0].Id,root);var loaded=ProjectStore.Load(root);
            Assert.Equal(d.Layers[0].Id,loaded.ActiveLayerId);Assert.Equal(next.Width,loaded.Document.Width);
            Assert.Equal(next.Layers[0].Pixels.ToRgba(),loaded.Document.Layers[0].Pixels.ToRgba());
            Assert.Equal(next.Layers[1].Transform,loaded.Document.Layers[1].Transform);
            Assert.Equal(next.Layers[1].Effects,loaded.Document.Layers[1].Effects);
            Assert.Equal(next.Layers[1].Mask!.Pixels.ToRgba(),loaded.Document.Layers[1].Mask!.Pixels.ToRgba());
        }
        finally
        {
            string full=Path.GetFullPath(root),temp=Path.GetFullPath(Path.GetTempPath());
            if(full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith("compositor-canvas-size-")&&Directory.Exists(full))Directory.Delete(full,true);
        }
    }
}
