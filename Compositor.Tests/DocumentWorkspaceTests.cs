using Compositor.Core;
using Xunit;
namespace Compositor.Tests;
public sealed class DocumentWorkspaceTests
{
    [Fact] public void SwitchingKeepsIndependentHistorySelectionsAndDirtyState()
    {
        using var w=new DocumentWorkspace(Document.Create(2,2));var first=w.Current;var original=first.Session.Document;
        first.Session.Apply(d=>d.Replace(d.Layers[0] with{Name="Edited"}));var edited=first.Session.Document;
        var second=w.New(Document.Create(3,3));second.Session.SelectLayers(Array.Empty<Guid>());
        Assert.False(second.Session.IsModified);Assert.Equal(0,second.Session.UndoCount);Assert.True(w.Select(first.Id));
        Assert.Same(edited,w.Current.Session.Document);Assert.True(first.Session.IsModified);first.Session.Undo();Assert.Same(original,first.Session.Document);
        w.Select(second.Id);Assert.Empty(second.Session.SelectedLayerIds);Assert.Equal(0,second.Session.RedoCount);
        w.Select(first.Id);first.Session.Redo();Assert.Same(edited,first.Session.Document);
    }
    [Fact] public void OpenSamePathSelectsExistingWithoutReplacingUnsavedEdits()
    {
        using var w=new DocumentWorkspace(Document.Create(1,1));string path=Path.Combine(Path.GetTempPath(),"workspace-test.comp");
        var tab=w.Open(Document.Create(2,2),path);tab.Session.Apply(d=>d.Replace(d.Layers[0] with{Name="Keep me"}));var edited=tab.Session.Document;
        w.New(Document.Create(3,3));var same=w.Open(Document.Create(4,4),path.ToUpperInvariant()+Path.DirectorySeparatorChar);
        Assert.Same(tab,same);Assert.Same(edited,w.Current.Session.Document);Assert.Equal(3,w.Documents.Count);
    }
    [Fact] public void InvalidOpenAndActiveTransactionsLeaveWorkspaceUntouched()
    {
        using var w=new DocumentWorkspace(Document.Create(1,1));var first=w.Current;var second=w.New(Document.Create(2,2));w.Select(first.Id);
        Assert.Throws<InvalidDataException>(()=>w.Open(Document.Create(1,1),Path.Combine(Path.GetTempPath(),"bad.comp"),Guid.NewGuid()));
        Assert.Equal(2,w.Documents.Count);Assert.Same(first,w.Current);
        first.Session.Begin();Assert.False(w.Select(second.Id));Assert.False(w.Close(second.Id,true));
        Assert.Throws<InvalidOperationException>(()=>w.New(Document.Create(1,1)));first.Session.Cancel();Assert.True(w.Select(second.Id));
    }
    [Fact] public void SavePathsAndRecoveryAreIndependentAndCollisionsAreRejected()
    {
        using var w=new DocumentWorkspace(Document.Create(1,1));var first=w.Current;string path=Path.Combine(Path.GetTempPath(),"saved-workspace.comp");
        w.RecordSaved(first.Id,path);var recovered=w.Open(Document.Create(2,2),path+".recovery",recovery:true);
        Assert.Null(recovered.ProjectPath);Assert.True(recovered.Session.IsModified);
        Assert.Throws<InvalidOperationException>(()=>w.RecordSaved(recovered.Id,path));Assert.Null(recovered.ProjectPath);Assert.True(recovered.Session.IsModified);
        int events=0;w.Changed+=()=>events++;
        Assert.Throws<InvalidOperationException>(()=>w.ValidateSaveDestination(recovered.Id,path));
        Assert.Equal(Path.GetFullPath(path+"-copy.comp"),w.ValidateSaveDestination(recovered.Id,path+"-copy.comp"));
        Assert.Null(recovered.ProjectPath);Assert.True(recovered.Session.IsModified);Assert.Equal(0,events);
        w.RecordSaved(recovered.Id,path+"-copy.comp");Assert.False(recovered.Session.IsModified);Assert.Equal(Path.GetFullPath(path),first.ProjectPath);
    }
    [Fact] public void CloseProtectsDirtyDocumentsAndLastTabIsReplaced()
    {
        using var w=new DocumentWorkspace(Document.Create(1,1));var first=w.Current;
        first.Session.Apply(d=>d.Replace(d.Layers[0] with{Name="Dirty"}));Assert.False(w.Close(first.Id));Assert.Same(first,w.Current);
        Assert.True(w.Close(first.Id,true));Assert.Single(w.Documents);Assert.NotSame(first,w.Current);Assert.False(w.Current.Session.IsModified);Assert.Equal(0,w.Current.Session.UndoCount);
        int events=0;w.Changed+=()=>events++;first.Session.Undo();Assert.Equal(0,events);
    }
    [Fact] public void QuitOrderStartsWithCurrentAndCloseSelectsNearestRemainingTab()
    {
        using var w=new DocumentWorkspace(Document.Create(1,1));var first=w.Current;var second=w.New(Document.Create(1,1));var third=w.New(Document.Create(1,1));
        w.Select(second.Id);Assert.Equal(new[]{second,first,third},w.QuitOrder());Assert.True(w.Close(second.Id));Assert.Same(third,w.Current);
        Assert.True(w.Close(first.Id));Assert.Same(third,w.Current);
    }
}