using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class SelectionPixelMoveEditTests
{
    private static Document Sample()
    {
        var doc=Document.Create(64,64);
        var layer=doc.Layers[0] with{Pixels=Raster.FromRgba(2,1,[80,20,10,128,0,0,0,0]),Transform=new(10,10,2,1),Mask=LayerMask.Solid(2,1,90)};
        return doc.Replace(layer) with{Selection=SelectionGeometry.Box(10,10,1,1,false,false)};
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void MoveOrDuplicateCommitsPixelsOutlineMaskAndOneUndo(bool duplicate)
    {
        var doc=Sample();var session=new EditorSession(doc);
        using(var edit=SelectionPixelMoveEdit.Begin(session,duplicate)!)
        {edit.Preview(3,0);edit.Preview(5,0);edit.Complete();}
        Assert.Equal(1,session.UndoCount);Assert.False(session.InTransaction);
        Assert.True(SelectionGeometry.Contains(session.Document.Selection!,new(15.5,10.5)));
        var layer=session.ActiveLayer!;
        Assert.Equal(duplicate?10:15,layer.Transform.X);
        Assert.Equal(duplicate?6:1,layer.Pixels.Width);
        Assert.Equal(255,layer.Mask!.Pixels.ToRgba()[^4]);
        if(duplicate)Assert.Equal(90,layer.Mask.Pixels.ToRgba()[0]);
        var applied=session.Document;session.Undo();Assert.Same(doc,session.Document);session.Redo();Assert.Same(applied,session.Document);
    }
    [Fact] public void ReturningToOriginAndCancelPreserveRedoAndOriginal()
    {
        var doc=Sample();var session=new EditorSession(doc);
        session.Apply(d=>d.Replace(d.Layers[0] with{Name="Renamed"}));session.Undo();
        using(var edit=SelectionPixelMoveEdit.Begin(session)!){edit.Preview(4,0);edit.Preview(0,0);edit.Complete();}
        Assert.Same(doc,session.Document);Assert.Equal(1,session.RedoCount);Assert.Equal(0,session.UndoCount);
        using(var edit=SelectionPixelMoveEdit.Begin(session)!){edit.Preview(-5,2);}
        Assert.Same(doc,session.Document);Assert.Equal(1,session.RedoCount);
    }
    [Fact] public void InvalidPreviewRestoresOriginalAndReleasesTransaction()
    {
        var doc=Sample();var session=new EditorSession(doc);using var edit=SelectionPixelMoveEdit.Begin(session)!;
        edit.Preview(4,0);Assert.Throws<ArgumentOutOfRangeException>(()=>edit.Preview(double.NaN,0));
        Assert.Same(doc,session.Document);Assert.False(session.InTransaction);Assert.Equal(0,session.UndoCount);
    }
    [Fact] public void EmptyTransparentHiddenAndMaskTargetsDoNotStartEditing()
    {
        var doc=Sample();var session=new EditorSession(doc with{Selection=DocumentSelection.Empty});
        Assert.Null(SelectionPixelMoveEdit.Begin(session));
        session.Load(doc with{Selection=SelectionGeometry.Box(30,30,2,2)});Assert.Null(SelectionPixelMoveEdit.Begin(session));
        session.Load(doc);session.EditMask=true;Assert.Throws<InvalidOperationException>(()=>SelectionPixelMoveEdit.Begin(session));
        session.Load(doc.Replace(doc.Layers[0] with{Visible=false}));Assert.Throws<InvalidOperationException>(()=>SelectionPixelMoveEdit.Begin(session));
        Assert.False(session.InTransaction);
    }
    [Fact] public void ExpansionBudgetFailsBeforeLargeAllocationAndRestoresDocument()
    {
        var doc=Sample() with{Width=1000,Height=1000};
        var other=Layer.Blank("Large",9999,10000) with{Pixels=new Raster(9999,10000,doc.Layers[0].Pixels.Tiles)};
        doc=doc with{Layers=doc.Layers.Add(other)};
        var session=new EditorSession(doc){ActiveLayerId=doc.Layers[0].Id};
        using var edit=SelectionPixelMoveEdit.Begin(session)!;
        Assert.Throws<InvalidDataException>(()=>edit.Preview(900,900));
        Assert.Same(doc,session.Document);Assert.False(session.InTransaction);
    }
    [Fact] public void MovedPixelsAndExpandedMaskSurviveProjectRoundtrip()
    {
        var doc=Sample();var session=new EditorSession(doc);
        using(var edit=SelectionPixelMoveEdit.Begin(session,true)!){edit.Preview(-4,3);edit.Complete();}
        string root=Path.Combine(Path.GetTempPath(),"compositor-move-"+Guid.NewGuid().ToString("N")+".comp");
        try
        {
            ProjectStore.Save(session.Document,session.ActiveLayerId,root);
            var loaded=ProjectStore.Load(root).Document;
            Assert.Null(loaded.Selection);
            Assert.Equal(session.ActiveLayer!.Transform,loaded.Layers[0].Transform);
            Assert.Equal(session.ActiveLayer.Pixels.ToRgba(),loaded.Layers[0].Pixels.ToRgba());
            Assert.Equal(session.ActiveLayer.Mask!.Pixels.ToRgba(),loaded.Layers[0].Mask!.Pixels.ToRgba());
        }
        finally
        {
            string full=Path.GetFullPath(root),temp=Path.GetFullPath(Path.GetTempPath());
            if(full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith("compositor-move-")&&Directory.Exists(full))Directory.Delete(full,true);
        }
    }}
