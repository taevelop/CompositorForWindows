using System.Windows;
using System.Windows.Input;
using Compositor.Core;
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
        }
        finally{Canvas.CancelGradient();session.Load(original);GradientTransparent.IsChecked=true;GradientOpacity.Value=100;RestoreTabTools(tools);Canvas.Fit();Refresh();}
    }
}
