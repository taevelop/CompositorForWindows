using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Compositor.Core;
namespace Compositor.App;
public sealed partial class EditorCanvas
{
    private readonly DispatcherTimer selectionScroll=new(DispatcherPriority.Input){Interval=TimeSpan.FromSeconds(1d/60)};
    private Point? selectionScrollPoint;
    internal bool SelectionAutoScrollRunning=>selectionScroll.IsEnabled;
    private bool CanAutoScrollSelection=>Session.InTransaction&&gestureButton==MouseButton.Left&&selectionStart is not null&&
        (movingSelection||Tool is EditorTool.RectangleSelection or EditorTool.EllipseSelection);
    private void InitializeSelectionAutoScroll()
    {
        selectionScroll.Tick+=(_,_)=>StepSelectionAutoScroll(IsMouseCaptured,Mouse.LeftButton==MouseButtonState.Pressed);
    }
    private void UpdateSelectionAutoScroll(Point point)
    {
        if(!CanAutoScrollSelection){StopSelectionAutoScroll();return;}
        var delta=SelectionAutoScroll.Delta(new(point.X,point.Y),ActualWidth,ActualHeight);
        if(delta==new PointD(0,0)){StopSelectionAutoScroll();return;}
        selectionScrollPoint=point;selectionScroll.Start();
    }
    internal void StepSelectionAutoScroll(bool captured,bool leftPressed)
    {
        if(!captured||!leftPressed||!CanAutoScrollSelection||selectionScrollPoint is not {} point)
        {StopSelectionAutoScroll();return;}
        var delta=SelectionAutoScroll.Delta(new(point.X,point.Y),ActualWidth,ActualHeight);
        if(delta==new PointD(0,0)){StopSelectionAutoScroll();return;}
        try
        {
            panX+=delta.X;panY+=delta.Y;
            MovePointer(DocumentPoint(point));InvalidateVisual();ViewportChanged?.Invoke();
        }
        catch(Exception e){CancelInteraction();ReportError?.Invoke(e.Message);}
    }
    private void StopSelectionAutoScroll(){selectionScroll.Stop();selectionScrollPoint=null;}
}
