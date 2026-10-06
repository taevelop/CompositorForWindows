using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class SelectionClipboardPixelsTests
{
    private static Document Sample()
    {
        var d=Document.Create(10,10);
        var layer=d.Layers[0] with{Pixels=Raster.FromRgba(2,2,Enumerable.Repeat(new byte[]{80,20,10,128},4).SelectMany(v=>v).ToArray()),Transform=new(3,4,2,2),Opacity=.5,Mask=LayerMask.Solid(2,2,0)};
        return d.Replace(layer) with{Selection=SelectionGeometry.Box(3,4,2,2,false,false)};
    }
    [Fact] public void CopyRawIgnoresLayerOpacityAndMaskButMergedIncludesThem()
    {
        var d=Sample();var raw=SelectionClipboardPixels.Copy(d,d.Layers[0].Id)!;
        Assert.Equal(new PointD(3,4),raw.Origin);Assert.Equal(2,raw.Pixels.Width);Assert.Equal(128,raw.Pixels.ToRgba()[3]);
        var merged=SelectionClipboardPixels.Copy(d,null,merged:true)!;Assert.Empty(merged.Pixels.Tiles);
        var mask=SelectionClipboardPixels.Copy(d,d.Layers[0].Id,mask:true)!;
        Assert.Equal(new byte[]{0,0,0,255},mask.Pixels.ToRgba().AsSpan(0,4).ToArray());
    }
    [Fact] public void NoSelectionCopiesCanvasAndEmptySelectionCopiesNothing()
    {
        var d=Sample() with{Selection=null};var copy=SelectionClipboardPixels.Copy(d,d.Layers[0].Id)!;
        Assert.Equal(new PointD(0,0),copy.Origin);Assert.Equal(10,copy.Pixels.Width);
        Assert.Equal(0,copy.Pixels.ToRgba()[3]);Assert.Equal(128,copy.Pixels.ToRgba()[(4*10+3)*4+3]);
        Assert.Null(SelectionClipboardPixels.Copy(d with{Selection=DocumentSelection.Empty},d.Layers[0].Id));
    }
    [Fact] public void CopyFeatherPreservesSoftAlphaAndDoesNotMutateSource()
    {
        var d=Sample();var source=d.Layers[0].Pixels.ToRgba();
        d=d with{Selection=SelectionGeometry.Feather(d.Selection!,2)};
        var copied=SelectionClipboardPixels.Copy(d,d.Layers[0].Id)!;
        Assert.Contains(copied.Pixels.ToRgba().Where((v,i)=>i%4==3),a=>a>0&&a<128);
        Assert.Equal(source,d.Layers[0].Pixels.ToRgba());
    }
    [Fact] public void ClipboardFailureCannotCutPixelsOrAddUndo()
    {
        var d=Sample();var s=new EditorSession(d);
        Assert.Throws<IOException>(()=>SelectionClipboardPixels.Cut(s,_=>throw new IOException("Clipboard busy")));
        Assert.Same(d,s.Document);Assert.False(s.InTransaction);Assert.Equal(0,s.UndoCount);
        PixelClipboardContent? copied=null;Assert.True(SelectionClipboardPixels.Cut(s,c=>copied=c));
        Assert.NotNull(copied);Assert.Empty(s.ActiveLayer!.Pixels.Tiles);Assert.Equal(1,s.UndoCount);
        s.Undo();Assert.Same(d,s.Document);
    }
    [Fact] public void PasteInPlaceAndExternalCenterAreSingleUndoAndDropSelection()
    {
        var d=Sample();var clip=SelectionClipboardPixels.Copy(d,d.Layers[0].Id)!;var s=new EditorSession(d);
        var id=SelectionClipboardPixels.Paste(s,clip);
        Assert.Equal(id,s.ActiveLayerId);Assert.Null(s.Document.Selection);Assert.Equal(3,s.ActiveLayer!.Transform.X);Assert.Equal(4,s.ActiveLayer.Transform.Y);
        var pasted=s.Document;s.Undo();Assert.Same(d,s.Document);s.Redo();Assert.Same(pasted,s.Document);
        s.Load(d);SelectionClipboardPixels.Paste(s,clip,false);Assert.Equal(4,s.ActiveLayer!.Transform.X);Assert.Equal(4,s.ActiveLayer.Transform.Y);
    }
    [Fact] public void PastingInsideGroupPreservesHierarchyAndTargetHistory()
    {
        var d=Sample();var group=Layer.Group("Folder",10,10);
        d=LayerHierarchy.Wrap(d,d.Layers[0].Id,group);
        var s=new EditorSession(d){ActiveLayerId=group.Id};var clip=new PixelClipboardContent(new Raster(1,1),new(0,0));
        var id=SelectionClipboardPixels.Paste(s,clip);Assert.Equal(group.Id,s.ActiveLayer!.ParentId);s.Document.Validate();
        s.Undo();Assert.Equal(group.Id,s.ActiveLayerId);s.Redo();Assert.Equal(id,s.ActiveLayerId);
    }
    [Fact] public void MergedIncludesOpacityAndMaskCopyFillsOutsideLayerWithBlack()
    {
        var d=Sample();d=d.Replace(d.Layers[0] with{Mask=null});
        var merged=SelectionClipboardPixels.Copy(d,null,merged:true)!;
        Assert.InRange(merged.Pixels.ToRgba()[3],(byte)63,(byte)65);
        d=Sample() with{Selection=null};
        var mask=SelectionClipboardPixels.Copy(d,d.Layers[0].Id,true)!;
        Assert.Equal(new byte[]{0,0,0,255},mask.Pixels.ToRgba().AsSpan(0,4).ToArray());
    }
    [Fact] public void MaskCutHidesSelectedCoverageAndRestoresWithUndo()
    {
        var d=Sample();d=d.Replace(d.Layers[0] with{Mask=LayerMask.Solid(1,1,200)});
        d=d with{Selection=SelectionGeometry.Box(3,4,1,1,false,false)};
        var session=new EditorSession(d){EditMask=true};PixelClipboardContent? copied=null;
        SelectionClipboardPixels.Cut(session,c=>copied=c);
        Assert.Equal(200,copied!.Pixels.ToRgba()[0]);
        var result=session.ActiveLayer!.Mask!.Pixels.ToRgba();
        Assert.Equal(0,result[0]);Assert.Equal(200,result[4]);Assert.Equal(255,result[3]);
        session.Undo();Assert.Same(d,session.Document);Assert.True(session.EditMask);
    }}
