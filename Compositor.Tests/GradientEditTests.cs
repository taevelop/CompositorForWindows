using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class GradientEditTests
{
    private static GradientFillSettings Settings=>new(new(.5,.5),new(2.5,.5),255,0,0,0,0,255,Style:GradientStyle.ForegroundToBackground,Opacity:.5);
    [Fact] public async Task RepeatedPreviewsUseOriginalAndCommitOneUndo()
    {
        var original=Document.Create(3,1);var session=new EditorSession(original);using var edit=new GradientEdit(session);
        await edit.UpdateAsync(Settings);var first=session.Document.Layers[0].Pixels.ToRgba();
        await edit.UpdateAsync(Settings with{Reversed=true});await edit.UpdateAsync(Settings);
        Assert.Equal(first,session.Document.Layers[0].Pixels.ToRgba());Assert.Equal(0,session.UndoCount);
        await edit.CommitAsync();Assert.Equal(1,session.UndoCount);session.Undo();Assert.Same(original,session.Document);session.Redo();Assert.Equal(first,session.Document.Layers[0].Pixels.ToRgba());
    }
    [Fact] public async Task CancelPendingRestoresOriginalAndDoesNotApplyLateResult()
    {
        var original=Document.Create(1000,1000);var session=new EditorSession(original);var edit=new GradientEdit(session);
        var pending=edit.UpdateAsync(Settings);edit.Dispose();await pending;
        Assert.Same(original,session.Document);Assert.False(session.InTransaction);Assert.Equal(0,session.UndoCount);
    }
    [Fact] public async Task LatestRequestWinsAndShortLineRestoresOriginal()
    {
        var original=Document.Create(3,1);var session=new EditorSession(original);using var edit=new GradientEdit(session);
        var first=edit.UpdateAsync(Settings);var second=edit.UpdateAsync(Settings with{Reversed=true});await Task.WhenAll(first,second);
        Assert.Equal(128,session.Document.Layers[0].Pixels.ToRgba()[2]);
        await edit.UpdateAsync(Settings with{End=Settings.Start});Assert.Same(original,session.Document);await edit.CommitAsync();Assert.Equal(0,session.UndoCount);
    }
    [Fact] public async Task CommitFreezesSettingsWhileWaitingForLatestPreview()
    {
        var session=new EditorSession(Document.Create(1000,1000));using var edit=new GradientEdit(session);
        var pending=edit.UpdateAsync(Settings);var commit=edit.CommitAsync();
        Assert.Throws<InvalidOperationException>(()=>{_ = edit.UpdateAsync(Settings with{Reversed=true});});
        await Task.WhenAll(pending,commit);Assert.Equal(1,session.UndoCount);Assert.False(session.InTransaction);
    }
    [Fact] public async Task ReplacedDocumentIsNotOverwrittenByPendingOrDispose()
    {
        var session=new EditorSession(Document.Create(1000,1000));var edit=new GradientEdit(session);var pending=edit.UpdateAsync(Settings);
        var replacement=Document.Create(2,2);session.Load(replacement);await pending;edit.Dispose();Assert.Same(replacement,session.Document);
    }
}
