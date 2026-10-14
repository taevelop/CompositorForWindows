using Compositor.Core;
using Compositor.Imaging;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
namespace Compositor.App;

public partial class MainWindow
{
    private async Task SubjectSelectionSmoke(string report)
    {
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        var saved=session.Document;var active=session.ActiveLayerId;
        try
        {
            var original=Document.Create(8,8) with{Selection=SelectionGeometry.Box(0,0,2,2)};int calls=0;
            subjectPredictorOverride=(source,token)=>{calls++;return LayerMask.Solid(source.Width,source.Height);};
            session.Load(original);SubjectReplaceMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await subjectPending;
            Check(calls==1&&session.UndoCount==1&&SelectionGeometry.Contains(session.Document.Selection!,new(7,7)),"Subject Replace menu failed.");
            session.Undo();Check(ReferenceEquals(session.Document,original),"Subject Undo failed.");session.Redo();Check(SelectionGeometry.Contains(session.Document.Selection!,new(7,7)),"Subject Redo failed.");
            session.Load(original);SubjectAddMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await subjectPending;Check(SelectionGeometry.Contains(session.Document.Selection!,new(7,7)),"Subject Add menu failed.");
            session.Load(original);SubjectSubtractMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await subjectPending;Check(session.Document.Selection!.IsEmpty,"Subject Subtract menu failed.");
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            subjectPredictorOverride=(source,token)=>{entered.TrySetResult();token.WaitHandle.WaitOne();token.ThrowIfCancellationRequested();return LayerMask.Solid(8,8);};
            session.Load(original);SubjectReplaceMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(busy&&FillCancelButton.Visibility==Visibility.Visible,"Subject cancellation UI missing.");FillCancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await subjectPending;
            Check(ReferenceEquals(session.Document,original)&&session.UndoCount==0&&!busy&&FillCancelButton.Visibility==Visibility.Collapsed,"Cancelled subject changed state or retained busy UI.");
            using var release=new ManualResetEventSlim();entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
            subjectPredictorOverride=(source,token)=>{entered.TrySetResult();if(!release.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException();return LayerMask.Solid(8,8);};
            SubjectReplaceMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var replacement=Document.Create(8,8);session.Load(replacement);release.Set();await subjectPending;
            Check(ReferenceEquals(session.Document,replacement)&&session.UndoCount==0&&!busy,"Late subject replaced a different document.");
            File.WriteAllText(report,JsonSerializer.Serialize(new{checks=new[]{"routed Replace/Add/Subtract menus","single Undo/Redo","running cancellation button","late result different document discarded"},note="Injected predictor WPF; actual ONNX selection quality and physical input separate."}));
        }
        finally{subjectPredictorOverride=null;session.Load(saved,active);Refresh();}
    }
}
