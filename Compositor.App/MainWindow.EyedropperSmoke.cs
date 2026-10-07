using System.Windows;
using System.Windows.Input;
using Compositor.Core;
namespace Compositor.App;
public partial class MainWindow
{
    private void EyedropperSmokeTest()
    {
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        var original=session.Document;var tools=CaptureTabTools();
        try
        {
            var d=Document.Create(3,1);d=d.Replace(d.Layers[0] with{Pixels=Raster.FromRgba(3,1,[255,0,0,255,0,255,0,255,0,0,0,0]),Transform=new(0,0,3,1,Sampling:Sampling.Nearest)});
            session.Load(d);Canvas.ActualPixels();ToolPicker.SelectedIndex=9;
            Check(Canvas.Tool==EditorTool.Eyedropper&&ToolTitle.Text=="Eyedropper","Eyedropper tool did not activate.");
            Check(Canvas.BeginInteraction(MouseButton.Left,new(30.1,30.1))&&BrushColor.Text=="#FF0000","Eyedropper click did not sample foreground.");
            Check(!Canvas.BeginInteraction(MouseButton.Middle,new(30,30)),"Second button interrupted sample drag.");
            Canvas.MoveInteraction(new(31.1,30.1));Check(BrushColor.Text=="#00FF00","Eyedropper drag did not update foreground.");
            Canvas.MoveInteraction(new(32.1,30.1));Canvas.MoveInteraction(new(1,1));Check(BrushColor.Text=="#00FF00","Transparent or outside pixel changed foreground.");
            Check(!Canvas.FinishInteraction(MouseButton.Right,new(30,30))&&Canvas.IsSamplingColor,"Wrong button ended sample drag.");
            Canvas.FinishInteraction(MouseButton.Left,new(31.1,30.1));Check(!Canvas.HasInteraction&&!Canvas.IsSamplingColor,"Sample release left interaction state.");
            ToolPicker.SelectedIndex=1;
            Check(Canvas.BeginInteraction(MouseButton.Left,new(30.1,30.1),modifiers:ModifierKeys.Alt)&&Canvas.IsSamplingColor,"Alt brush did not sample.");
            Canvas.CancelInteraction();Check(!Canvas.HasInteraction&&BrushColor.Text=="#FF0000","Cancel did not stop sampling or lost last sampled color.");
            ToolPicker.SelectedIndex=2;Canvas.BeginInteraction(MouseButton.Left,new(31.1,30.1),modifiers:ModifierKeys.Alt);
            Check(Canvas.IsSamplingColor&&BrushColor.Text=="#00FF00","Alt eraser did not sample.");
            ToolPicker.SelectedIndex=3;Check(!Canvas.IsSamplingColor&&!Canvas.HasInteraction,"Tool change retained sampling.");
            Check(ReferenceEquals(d,session.Document)&&session.UndoCount==0&&!session.IsModified,"Sampling painted pixels or changed history.");
            SampleRingSmokeTest();
            ToolPicker.SelectedIndex=9;Canvas.RestoreView(2,-10,7);
            Canvas.BeginInteraction(MouseButton.Left,new(-7.8,7.2));Canvas.FinishInteraction(MouseButton.Left,new(-7.8,7.2));
            Check(BrushColor.Text=="#00FF00","Sampling ignored viewport zoom/pan.");
            Canvas.BeginInteraction(MouseButton.Left,new(-9.8,7.2));
            Canvas.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.LostMouseCaptureEvent});
            Check(!Canvas.IsSamplingColor&&!Canvas.HasInteraction,"Capture loss retained sampling.");
            Canvas.BeginInteraction(MouseButton.Left,new(-9.8,7.2));
            var escape=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this)!,Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent};
            RaiseEvent(escape);Check(escape.Handled&&!Canvas.IsSamplingColor&&!Canvas.HasInteraction,"Escape retained sampling.");
        }
        finally{Canvas.CancelInteraction();session.Load(original);RestoreTabTools(tools);Canvas.Fit();Refresh();}
    }
}
