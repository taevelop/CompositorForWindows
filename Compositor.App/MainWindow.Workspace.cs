using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Compositor.Core;
namespace Compositor.App;
public partial class MainWindow
{
    private readonly DocumentWorkspace workspace = new(Document.Create(1400,900));
    private sealed record TabView(double Zoom,double X,double Y,Guid? DocumentId,Guid[] Collapsed,string? Path,TabTools Tools);
    private readonly Dictionary<Guid,TabView> tabViews=[];
    private void RememberTab()
    {
        var view=Canvas.CaptureView();
        tabViews[workspace.Current.Id]=new(view.Zoom,view.X,view.Y,panelDocumentId,collapsedGroups.ToArray(),projectPath,CaptureTabTools());
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
            RestoreTabTools(view.Tools);
            Canvas.RestoreView(view.Zoom,view.X,view.Y);
        }
        else { RestoreTabTools(defaultTabTools);Canvas.Fit(); }
        Refresh();
    }
    private void SelectTab(Guid id)
    {
        if(id==workspace.Current.Id||!PrepareTabChange())return;
        if(workspace.Select(id))BindCurrentTab();
    }
    private async Task SelectTabAsync(Guid id)
    {
        if(id==workspace.Current.Id||!await ResolveGradientBeforeAction())return;
        SelectTab(id);
    }
    private void AddDocumentTab(Document document)
    {
        if(!PrepareTabChange())return;
        workspace.New(document);BindCurrentTab();
    }
    private async Task CloseTab(Guid id)
    {
        if(!await ResolveGradientBeforeAction())return;
        if(!PrepareTabChange())return;
        if(!workspace.Select(id))return;
        BindCurrentTab();
        if(!await ConfirmDiscard())return;
        if(workspace.Close(id,true)){tabViews.Remove(id);BindCurrentTab();}
    }
    private async Task<bool> ConfirmWorkspaceClose()
    {
        if(!await ResolveGradientBeforeAction())return false;
        foreach(var tab in workspace.QuitOrder())
        {
            await SelectTabAsync(tab.Id);
            if(workspace.Current!=tab || !await ConfirmDiscard())return false;
        }
        return true;
    }
    private readonly Dictionary<Guid,(Border Frame,Button Select,Button Close,TextBlock Label)> tabControls=[];
    private Guid? visibleTab;
    private void RefreshTabs()
    {
        var ids=workspace.Documents.Select(t=>t.Id).ToHashSet();
        foreach(var id in tabControls.Keys.Where(id=>!ids.Contains(id)).ToArray())
        { DocumentTabs.Children.Remove(tabControls[id].Frame);tabControls.Remove(id); }
        for(int index=0;index<workspace.Documents.Count;index++)
        {
            var tab=workspace.Documents[index];
            if(!tabControls.TryGetValue(tab.Id,out var controls))
            {
                var row=new StackPanel{Orientation=Orientation.Horizontal};
                var label=new TextBlock{TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=190};
                var button=new Button{Content=label,MaxWidth=220,Style=(Style)FindResource("CompactButton")};
                button.Click+=async(_,_)=>await SelectTabAsync(tab.Id);
                var close=new Button{Content="×",Style=(Style)FindResource("CompactButton")};
                close.Click+=async(_,_)=>await CloseTab(tab.Id);
                row.Children.Add(button);row.Children.Add(close);
                var frame=new Border{Child=row,CornerRadius=new(5),Margin=new(0,0,5,0),BorderThickness=new(0,0,0,2)};
                AttachTabDrop(frame,tab.Id);
                controls=(frame,button,close,label);tabControls.Add(tab.Id,controls);
            }
            if(DocumentTabs.Children.IndexOf(controls.Frame)!=index)
            { DocumentTabs.Children.Remove(controls.Frame);DocumentTabs.Children.Insert(index,controls.Frame); }
            bool active=tab==workspace.Current;
            controls.Label.Text=tab.Title+(tab.Session.IsModified?" *":"");
            controls.Select.ToolTip=tab.ProjectPath??tab.DefaultName;
            controls.Close.ToolTip="Close "+tab.Title+" (Ctrl+W)";
            System.Windows.Automation.AutomationProperties.SetName(controls.Select,
                (active?"Selected document: ":"Document: ")+tab.Title+(tab.Session.IsModified?", unsaved changes":""));
            System.Windows.Automation.AutomationProperties.SetName(controls.Close,"Close document: "+tab.Title);
            controls.Frame.BorderBrush=active?(Brush)FindResource("Accent"):Brushes.Transparent;
            controls.Frame.Background=active?new SolidColorBrush(Color.FromRgb(44,48,55)):Brushes.Transparent;
        }
        if(visibleTab!=workspace.Current.Id)
        {
            visibleTab=workspace.Current.Id;var selected=visibleTab.Value;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>
            {
                if(workspace.Current.Id==selected&&tabControls.TryGetValue(selected,out var controls))controls.Frame.BringIntoView();
            }));
        }
    }
}
