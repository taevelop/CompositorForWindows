using System.Collections.ObjectModel;
namespace Compositor.Core;
public sealed class WorkspaceDocument
{
    public Guid Id {get;}=Guid.NewGuid();
    public EditorSession Session {get;}
    public string DefaultName {get;}
    public string? ProjectPath {get;internal set;}
    public string Title=>ProjectPath is {} path?Path.GetFileNameWithoutExtension(path):DefaultName;
    internal WorkspaceDocument(EditorSession session,string name,string? path){Session=session;DefaultName=name;ProjectPath=path;}
}
/// <summary>Owns independent document sessions; UI resolves edits and save prompts before switching/closing.</summary>
public sealed class DocumentWorkspace:IDisposable
{
    private readonly List<WorkspaceDocument> documents=[];
    private int nextNumber=2;
    private bool disposed;
    public ReadOnlyCollection<WorkspaceDocument> Documents {get;}
    public WorkspaceDocument Current {get;private set;}
    public event Action? Changed;
    public DocumentWorkspace(Document initial)
    {
        Documents=documents.AsReadOnly();Current=new(new EditorSession(initial),"Untitled",null);Attach(Current);
    }
    private void Attach(WorkspaceDocument tab){documents.Add(tab);tab.Session.Changed+=Notify;}
    private void Notify()=>Changed?.Invoke();
    private void Check(){ObjectDisposedException.ThrowIf(disposed,this);}
    private void RequireIdle(){Check();if(Current.Session.InTransaction)throw new InvalidOperationException("Finish the current edit before changing documents.");}
    private static string CanonicalPath(string path)
    {
        if(string.IsNullOrWhiteSpace(path))throw new ArgumentException("Project path is empty.",nameof(path));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    public WorkspaceDocument? FindPath(string path)
    {
        Check();var normalized=CanonicalPath(path);
        return documents.FirstOrDefault(d=>string.Equals(d.ProjectPath,normalized,StringComparison.OrdinalIgnoreCase));
    }
    public bool Select(Guid id)
    {
        Check();var tab=documents.FirstOrDefault(d=>d.Id==id);
        if(tab is null)return false;
        if(tab==Current)return true;
        if(Current.Session.InTransaction||tab.Session.InTransaction)return false;
        Current=tab;Notify();return true;
    }
    public WorkspaceDocument New(Document document)
    {
        RequireIdle();var session=new EditorSession(document);
        var tab=new WorkspaceDocument(session,"Untitled "+nextNumber++,null);Attach(tab);Current=tab;Notify();return tab;
    }
    public WorkspaceDocument Open(Document document,string source,Guid? active=null,bool recovery=false)
    {
        RequireIdle();var normalized=CanonicalPath(source);
        if(!recovery&&FindPath(normalized) is {} existing)
        {if(!Select(existing.Id))throw new InvalidOperationException("The document is being edited.");return existing;}
        // Validate and initialize an unattached session before changing the workspace.
        var session=new EditorSession(document);session.Load(document,active,recovered:recovery);
        var tab=new WorkspaceDocument(session,recovery?"Recovered "+Path.GetFileNameWithoutExtension(normalized):Path.GetFileNameWithoutExtension(normalized),recovery?null:normalized);
        Attach(tab);Current=tab;Notify();return tab;
    }
    public string ValidateSaveDestination(Guid id,string path)
    {
        Check();var tab=documents.First(d=>d.Id==id);var normalized=CanonicalPath(path);
        if(tab.Session.InTransaction)throw new InvalidOperationException("Finish the edit before recording a save.");
        if(FindPath(normalized) is {} other&&other!=tab)throw new InvalidOperationException("This project is already open in another tab.");
        return normalized;
    }
    public void RecordSaved(Guid id,string path)
    {
        var normalized=ValidateSaveDestination(id,path);var tab=documents.First(d=>d.Id==id);
        tab.ProjectPath=normalized;tab.Session.MarkSaved();
    }
    public WorkspaceDocument[] QuitOrder(){Check();return new[]{Current}.Concat(documents.Where(d=>d!=Current)).ToArray();}
    public bool Close(Guid id,bool discardChanges=false)
    {
        Check();int index=documents.FindIndex(d=>d.Id==id);if(index<0)return false;var tab=documents[index];
        if(Current.Session.InTransaction||tab.Session.InTransaction||(!discardChanges&&tab.Session.IsModified))return false;
        WorkspaceDocument? replacement=documents.Count==1?new(new EditorSession(Document.Create(1400,900)),"Untitled "+nextNumber++,null):null;
        tab.Session.Changed-=Notify;documents.RemoveAt(index);
        if(replacement is not null){Attach(replacement);Current=replacement;}
        else if(Current==tab)Current=documents[Math.Min(index,documents.Count-1)];
        Notify();return true;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;foreach(var tab in documents)tab.Session.Changed-=Notify;
    }
}