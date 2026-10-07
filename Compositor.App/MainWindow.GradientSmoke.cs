using System.Windows;
using System.Windows.Input;
using Compositor.Core;
using Compositor.Imaging;
using System.IO;
namespace Compositor.App;
public partial class MainWindow
{
    private async Task GradientSmokeTest()
    {
        var original=session.Document;var tools=CaptureTabTools();
        try
        {
            var doc=Document.Create(3,1);session.Load(doc);ToolPicker.SelectedIndex=10;Canvas.RestoreView(20,30,30);
            BrushColor.Text="#FF0000";SetBackgroundColor("#0000FF");GradientTransparent.IsChecked=false;GradientOpacity.Value=100;
            if(Canvas.Tool!=EditorTool.Gradient||ToolTitle.Text!="Gradient"||GradientOptions.Visibility!=Visibility.Visible)throw new InvalidOperationException("Gradient tool UI failed.");
            Canvas.BeginInteraction(MouseButton.Left,new(40,40));Canvas.MoveInteraction(new(80,40));Canvas.FinishInteraction(MouseButton.Left,new(80,40));await Canvas.GradientPending;
            if(!Canvas.HasGradient||session.UndoCount!=0||!session.Document.Layers[0].Pixels.ToRgba().SequenceEqual(new byte[]{255,0,0,255,128,0,128,255,0,0,255,255}))throw new InvalidOperationException("Gradient drag preview failed.");
            Canvas.BeginInteraction(MouseButton.Left,new(80,40));Canvas.MoveInteraction(new(120,40));Canvas.FinishInteraction(MouseButton.Left,new(120,40));await Canvas.GradientPending;
            if(session.Document.Layers[0].Pixels.ToRgba()[8]!=128)throw new InvalidOperationException("Gradient endpoint handle failed.");
            await Canvas.CommitGradientAsync();if(session.UndoCount!=1||Canvas.HasGradient)throw new InvalidOperationException("Gradient commit failed.");
            session.Undo();if(!ReferenceEquals(doc,session.Document))throw new InvalidOperationException("Gradient Undo failed.");
            Canvas.BeginInteraction(MouseButton.Left,new(40,40));Canvas.MoveInteraction(new(80,40));Canvas.FinishInteraction(MouseButton.Left,new(80,40));Canvas.CancelGradient();await Canvas.GradientPending;
            if(!ReferenceEquals(doc,session.Document)||session.InTransaction)throw new InvalidOperationException("Gradient cancel failed.");
            await GradientSaveSmokeTest();
        }
        finally{Canvas.CancelGradient();session.Load(original);GradientTransparent.IsChecked=true;GradientOpacity.Value=100;RestoreTabTools(tools);Canvas.Fit();Refresh();}
    }
    private async Task GradientSaveSmokeTest()
    {
        var previous=workspace.Current.Id;RememberTab();
        string destination=Path.Combine(Path.GetTempPath(),"Compositor-gradient-save-"+Guid.NewGuid()+".comp");
        var report=Canvas.ReportError;
        workspace.New(Document.Create(3,1));BindCurrentTab();
        try
        {
            ToolPicker.SelectedIndex=10;Canvas.RestoreView(20,30,30);
            BrushColor.Text="#FF0000";SetBackgroundColor("#0000FF");GradientTransparent.IsChecked=false;
            projectPath=destination;
            Canvas.BeginInteraction(MouseButton.Left,new(40,40));Canvas.FinishInteraction(MouseButton.Left,new(80,40));
            if(!await Save(false)||Canvas.HasGradient||session.InTransaction||session.UndoCount!=1||session.IsModified)
                throw new InvalidOperationException("Save did not commit the pending gradient.");
            var expected=new byte[]{255,0,0,255,128,0,128,255,0,0,255,255};
            if(!ProjectStore.Load(destination).Document.Layers[0].Pixels.ToRgba().SequenceEqual(expected))
                throw new InvalidOperationException("Saved gradient pixels differ from the final preview.");
            Canvas.BeginInteraction(MouseButton.Left,new(40,40));Canvas.FinishInteraction(MouseButton.Left,new(80,40));
            await Canvas.GradientPending;
            session.Load(Document.Create(3,1));
            bool reported=false;Canvas.ReportError=_=>reported=true;
            if(await Save(false)||!reported||busy||!Editor.IsEnabled||
                !ProjectStore.Load(destination).Document.Layers[0].Pixels.ToRgba().SequenceEqual(expected))
                throw new InvalidOperationException("Failed gradient commit did not preserve the saved project.");
        }
        finally
        {
            Canvas.ReportError=report;Canvas.CancelGradient();workspace.Close(workspace.Current.Id,true);
            workspace.Select(previous);BindCurrentTab();
            string full=Path.GetFullPath(destination),temp=Path.GetFullPath(Path.GetTempPath());
            if(full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith("Compositor-gradient-save-",StringComparison.Ordinal)&&Directory.Exists(full))Directory.Delete(full,true);
        }
    }
}
