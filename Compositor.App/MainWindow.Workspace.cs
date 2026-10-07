using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Compositor.Core;
namespace Compositor.App;
public partial class MainWindow
{
    private readonly DocumentWorkspace workspace = new(Document.Create(1400,900));
    private sealed record TabView(double Zoom,double X,double Y,Guid? DocumentId,Guid[] Collapsed,string? Path);
    private readonly Dictionary<Guid,TabView> tabViews=[];
    private void RememberTab()
    {
        var view=Canvas.CaptureView();
        tabViews[workspace.Current.Id]=new(view.Zoom,view.X,view.Y,panelDocumentId,collapsedGroups.ToArray(),projectPath);
    }
    private bool PrepareTabChange()
    {
        if(busy)return false;
        if(Canvas.IsTransforming)Canvas.CommitTransform();
        Canvas.CancelInteraction();
        if(session.InTransaction)return false;
        Canvas.CancelCrop();RememberTab();return true;
    }
    private void BindCurrentTab()
    {
        session.Changed-=Refresh;session=workspace.Current.Session;session.Changed+=Refresh;
        Canvas.Session=session;collapsedGroups.Clear();panelDocumentId=session.Document.Id;
        projectPath=workspace.Current.ProjectPath;
        if(tabViews.TryGetValue(workspace.Current.Id,out var view))
        {
            projectPath=view.Path;
            if(view.DocumentId==session.Document.Id)collapsedGroups.UnionWith(view.Collapsed);
            Canvas.RestoreView(view.Zoom,view.X,view.Y);
        }
        else Canvas.Fit();
        Refresh();
    }
    private void SelectTab(Guid id)
    {
        if(id==workspace.Current.Id||!PrepareTabChange())return;
        if(workspace.Select(id))BindCurrentTab();
    }
    private void AddDocumentTab(Document document)
    {
        if(!PrepareTabChange())return;
        workspace.New(document);BindCurrentTab();
    }
    private async Task CloseTab(Guid id)
    {
        if(!PrepareTabChange())return;
        if(!workspace.Select(id))return;
        BindCurrentTab();
        if(!await ConfirmDiscard())return;
        if(workspace.Close(id,true)){tabViews.Remove(id);BindCurrentTab();}
    }
    private async Task<bool> ConfirmWorkspaceClose()
    {
        if(busy)return false;
        foreach(var tab in workspace.QuitOrder())
        {
            SelectTab(tab.Id);
            if(workspace.Current!=tab || !await ConfirmDiscard())return false;
        }
        return true;
    }
    private void RefreshTabs()
    {
        DocumentTabs.Children.Clear();
        foreach(var tab in workspace.Documents)
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal};
            var button=new Button{Content=tab.Title+(tab.Session.IsModified?" *":""),MaxWidth=220,ToolTip=tab.ProjectPath??tab.DefaultName,Style=(Style)FindResource("CompactButton")};
            button.Click+=(_,_)=>SelectTab(tab.Id);
            var close=new Button{Content="×",ToolTip="Close document",Style=(Style)FindResource("CompactButton")};
            close.Click+=async(_,_)=>await CloseTab(tab.Id);
            row.Children.Add(button);row.Children.Add(close);
            DocumentTabs.Children.Add(new Border{Child=row,CornerRadius=new(5),Margin=new(0,0,5,0),BorderThickness=new(0,0,0,2),BorderBrush=tab==workspace.Current?(Brush)FindResource("Accent"):Brushes.Transparent,Background=tab==workspace.Current?new SolidColorBrush(Color.FromRgb(44,48,55)):Brushes.Transparent});
        }
    }
}
