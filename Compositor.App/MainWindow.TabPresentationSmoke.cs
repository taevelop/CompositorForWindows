using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using Compositor.Core;
namespace Compositor.App;
public partial class MainWindow
{
    private async Task TabPresentationSmokeTest()
    {
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        var original=workspace.Current;var added=new List<Guid>();
        try
        {
            var button=tabControls[original.Id].Select;button.Focus();
            var focused=button.IsKeyboardFocused;Refresh();
            Check(ReferenceEquals(button,tabControls[original.Id].Select)&&(!focused||button.IsKeyboardFocused),"Tab refresh recreated a focused button.");
            Check(AutomationProperties.GetName(button).Contains("Selected document:"),"Active document accessibility name missing.");
            Check(AutomationProperties.GetName(tabControls[original.Id].Close).Contains(original.Title),"Close button did not identify its document.");
            for(int i=0;i<10;i++){AddDocumentTab(Document.Create(8,8));added.Add(workspace.Current.Id);}
            UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Check(DocumentTabScroll.ScrollableWidth>0&&DocumentTabScroll.HorizontalOffset>0,"Active last tab remained outside overflow viewport.");
            var frame=tabControls[workspace.Current.Id].Frame;var position=frame.TranslatePoint(new Point(0,0),DocumentTabScroll);
            Check(position.X>=-1&&position.X+frame.ActualWidth<=DocumentTabScroll.ActualWidth+1,"Active tab was not fully visible.");
            SelectTab(workspace.Documents[0].Id);UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Check(DocumentTabScroll.HorizontalOffset<1,"First tab was not revealed after switching back.");
        }
        finally
        {
            SelectTab(original.Id);
            foreach(var id in added){workspace.Close(id);tabViews.Remove(id);}
            Refresh();
        }
    }
}
