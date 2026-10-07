using System.Windows;
using System.Windows.Input;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    private async Task SelectionAutoScrollSmokeTest()
    {
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        session.Load(Document.Create(3000,2000));Canvas.ActualPixels();ToolPicker.SelectedIndex=4;Canvas.SelectionMode=Compositor.Core.SelectionMode.Replace;
        var original=session.Document;var start=new Point(80,80);var edge=new Point(Canvas.ActualWidth+40,180);
        Check(Canvas.BeginInteraction(MouseButton.Left,start),"Auto-scroll selection did not start.");
        Canvas.MoveInteraction(edge);Check(Canvas.SelectionAutoScrollRunning,"Edge did not start auto-scroll timer.");
        var before=Canvas.DocumentPoint(edge);using var oldPath=SelectionGeometry.Path(session.Document.Selection!);
        Canvas.StepSelectionAutoScroll(true,true);
        var after=Canvas.DocumentPoint(edge);using var newPath=SelectionGeometry.Path(session.Document.Selection!);
        Check(after.X>before.X&&newPath.Bounds.Right>oldPath.Bounds.Right,"Stationary pointer did not extend marquee as viewport moved.");
        Check(Math.Abs(newPath.Bounds.Left-oldPath.Bounds.Left)<.01,"Auto-scroll moved the fixed marquee anchor.");
        Canvas.MoveInteraction(new Point(Canvas.ActualWidth/2,Canvas.ActualHeight/2));Check(!Canvas.SelectionAutoScrollRunning,"Moving inside did not stop auto-scroll.");
        Canvas.MoveInteraction(edge);Canvas.StepSelectionAutoScroll(true,false);Check(!Canvas.SelectionAutoScrollRunning,"Released button did not stop timer.");
        Canvas.MoveInteraction(edge);Canvas.FinishInteraction(MouseButton.Left,edge);
        Check(!Canvas.SelectionAutoScrollRunning&&!Canvas.HasInteraction&&session.UndoCount==1,"Finishing selection left timer running or split Undo.");
        session.Undo();Check(ReferenceEquals(original,session.Document),"Auto-scroll selection Undo failed.");
        Canvas.ActualPixels();Canvas.ZoomAt(new Point(100,100),120);
        Canvas.BeginInteraction(MouseButton.Left,start);Canvas.MoveInteraction(edge);
        before=Canvas.DocumentPoint(edge);var delta=SelectionAutoScroll.Delta(new(edge.X,edge.Y),Canvas.ActualWidth,Canvas.ActualHeight);
        Canvas.StepSelectionAutoScroll(true,true);after=Canvas.DocumentPoint(edge);
        Check(Math.Abs(after.X-before.X+delta.X/Canvas.Zoom)<.001,"Auto-scroll ignored zoom.");
        Canvas.CancelInteraction();Check(!Canvas.SelectionAutoScrollRunning&&ReferenceEquals(original,session.Document),"Cancel left scrolling or draft state.");
        session.Load(original with{Selection=SelectionGeometry.Box(40,40,100,100,false,false)});Canvas.ActualPixels();
        Canvas.BeginInteraction(MouseButton.Left,new Point(90,90));Canvas.MoveInteraction(edge);
        using var movedBefore=SelectionGeometry.Path(session.Document.Selection!);Canvas.StepSelectionAutoScroll(true,true);
        using var movedAfter=SelectionGeometry.Path(session.Document.Selection!);
        Check(movedAfter.Bounds.Left>movedBefore.Bounds.Left&&Math.Abs(movedAfter.Bounds.Width-movedBefore.Bounds.Width)<.01,"Outline scroll changed selection size.");
        Canvas.StepSelectionAutoScroll(false,true);Check(!Canvas.SelectionAutoScrollRunning,"Capture loss did not stop timer.");Canvas.CancelInteraction();
        session.Load(original);ToolPicker.SelectedIndex=6;Canvas.BeginInteraction(MouseButton.Left,start);Canvas.MoveInteraction(edge);
        Check(!Canvas.SelectionAutoScrollRunning,"Freehand unexpectedly auto-scrolled.");Canvas.CancelInteraction();
        ToolPicker.SelectedIndex=5;Canvas.BeginInteraction(MouseButton.Left,start);Canvas.MoveInteraction(edge);
        Check(Canvas.SelectionAutoScrollRunning,"Ellipse did not auto-scroll.");
        await Task.Delay(50);
        await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Check(!Canvas.SelectionAutoScrollRunning,"Dispatcher timer did not stop without a captured pointer.");
        Canvas.MoveInteraction(edge);ToolPicker.SelectedIndex=1;
        Check(!Canvas.SelectionAutoScrollRunning&&!Canvas.HasInteraction,"Tool switch retained auto-scroll.");
    }
}
