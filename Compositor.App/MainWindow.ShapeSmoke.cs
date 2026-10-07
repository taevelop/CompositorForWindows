using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    private async Task ShapeSmokeTest(string reportPath)
    {
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"shape-smoke-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var savedDocument=session.Document;var savedActive=session.ActiveLayerId;bool savedMask=session.EditMask;var savedTools=CaptureTabTools();
        try
        {
            session.Load(Document.Create(64,64) with{Selection=SelectionGeometry.Box(0,0,2,2)});
            ToolPicker.SelectedIndex=11;ShapeRectangle.IsChecked=true;BrushColor.Text="#1266AA";ShapeRadius.Value=12;Canvas.Fit();
            Check(ShapeOptions.Visibility==Visibility.Visible&&ShapeColorButton.IsEnabled,"Shape options or palette unavailable.");
            var before=session.Document;Canvas.BeginPointer(new(8,8));Canvas.MovePointer(new(24,18),ModifierKeys.Shift);
            Check(Canvas.IsDrawingShape&&session.InTransaction&&session.Document.Layers.Length==1,"Shape preview changed document pixels.");
            Canvas.EndPointer(true);var rectangle=session.ActiveLayer!;
            Check(rectangle.Shape is {Kind:ShapeKind.Rectangle,CornerRadius:12}&&rectangle.Pixels.Width==16&&rectangle.Pixels.Height==16,"Rectangle constraints/style failed.");
            Check(rectangle.Shape!.Blue==170/255d&&ReferenceEquals(before.Selection,session.Document.Selection),"Shape palette/selection changed.");
            Undo(this,new());Check(ReferenceEquals(before,session.Document)&&!session.CanUndo,"Shape insertion was not a single Undo.");Redo(this,new());
            var after=session.Document;Canvas.BeginPointer(new(3,3));Canvas.MovePointer(new(50,50));Canvas.CancelInteraction();
            Check(ReferenceEquals(after,session.Document)&&!session.InTransaction,"Shape cancel left an edit.");
            var view=Canvas.CaptureView();Point Screen(double x,double y)=>new(x*view.Zoom+view.X,y*view.Zoom+view.Y);
            Check(Canvas.BeginInteraction(MouseButton.Left,Screen(52,8),modifiers:ModifierKeys.None),"Shape mouse route did not begin.");
            Canvas.MoveInteraction(Screen(60,16));Check(Canvas.FinishInteraction(MouseButton.Left,Screen(60,16)),"Shape mouse route did not end.");
            Check(session.ActiveLayer!.Pixels.Width==8&&session.ActiveLayer.Pixels.Height==8&&!Canvas.IsDrawingShape,"Shape mouse route missed the final pointer.");
            CycleShapeKind();Check(ShapeEllipse.IsChecked==true,"Shape kind cycle failed.");
            Canvas.BeginPointer(new(40,40));Canvas.MovePointer(new(46,44),ModifierKeys.Shift|ModifierKeys.Alt);Canvas.EndPointer(true);
            Check(session.ActiveLayer!.Shape?.Kind==ShapeKind.Ellipse&&session.ActiveLayer.Transform==new LayerTransform(34,34,12,12),"Centered ellipse failed.");
            CycleShapeKind();ShapeWidth.Value=4;
            Canvas.BeginPointer(new(8,48));Canvas.MovePointer(new(28,52),ModifierKeys.Shift);Canvas.EndPointer(true);
            Check(session.ActiveLayer!.Shape is {Kind:ShapeKind.Line,LineWidth:4}&&session.ActiveLayer.Pixels.Height==4,"Line thickness/angle failed.");
            ShapeWidthValue.Text="5000";Check(ReadShape().LineWidth==5000,"Precise shape input cannot exceed the slider range.");ShapeWidthValue.Text="4";
            projectPath=Path.Combine(root,"Shapes.comp");Check(await Save(false),"Shape UI save failed.");var output=CompositePixels(session.Document);
            Check(await ReopenSavedProject(projectPath!),"Shape UI reopen failed.");Check(output.SequenceEqual(CompositePixels(session.Document))&&session.Document.Layers.Count(l=>l.Shape is not null)==4,"Shape UI round trip changed pixels/style.");
            ToolPicker.SelectedIndex=11;ShapeLine.IsChecked=true;BrushColor.Text="#1266AA";
            var smallDocument=session.Document;var smallActive=session.ActiveLayerId;
            session.Load(Document.Create(1024,1024));ShapeRectangle.IsChecked=true;
            var largeOriginal=session.Document;
            Canvas.BeginPointer(new(0,0));Canvas.MovePointer(new(1024,1024));Canvas.EndPointer(true);
            Check(Canvas.IsShapePreparing&&session.InTransaction&&ReferenceEquals(largeOriginal,session.Document),"Large shape did not prepare off the UI transaction.");
            bool dispatcherRan=false;var dispatched=Dispatcher.InvokeAsync(()=>dispatcherRan=true,DispatcherPriority.Input).Task;
            projectPath=Path.Combine(root,"Large.comp");var savingLarge=Save(false);
            await dispatched;Check(dispatcherRan&&await savingLarge,"Save did not await the large shape worker.");
            Check(ProjectStore.Load(projectPath!).Document.Layers.Count(l=>l.Shape is not null)==1,"Pending shape was omitted from saved project.");
            Check(session.ActiveLayer!.Pixels.Width==1024&&session.UndoCount==1&&!session.InTransaction,"Large shape did not commit one edit.");
            Undo(this,new());Check(ReferenceEquals(largeOriginal,session.Document),"Large shape Undo changed original.");
            Canvas.BeginPointer(new(0,0));Canvas.MovePointer(new(1024,1024));Canvas.EndPointer(true);
            var cancelled=Canvas.ShapeCompletion;var savedManifest=File.ReadAllBytes(Path.Combine(projectPath!,"manifest.json"));var cancelledSave=Save(false);
            var escape=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this)!,0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent};
            WindowKeyDown(this,escape);Check(escape.Handled&&!await cancelledSave,"Escape did not cancel a save waiting for shape creation.");
            Check(savedManifest.SequenceEqual(File.ReadAllBytes(Path.Combine(projectPath!,"manifest.json"))),"Cancelled pending save changed the project.");
            Check(!await cancelled&&ReferenceEquals(largeOriginal,session.Document)&&!session.InTransaction&&!session.CanUndo,"Cancelled large shape was published.");
            Canvas.BeginPointer(new(0,0));Canvas.MovePointer(new(1024,1024));Canvas.EndPointer(true);
            var stale=Canvas.ShapeCompletion;session.Load(largeOriginal);session.Begin();
            Check(!await stale&&session.InTransaction&&ReferenceEquals(largeOriginal,session.Document),"Stale shape result cancelled a newer transaction.");session.Cancel();
            session.Load(smallDocument,smallActive);ShapeLine.IsChecked=true;
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth),(int)Math.Ceiling(ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(this);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.ChangeExtension(reportPath,".png")))png.Save(file);
            File.WriteAllText(reportPath,JsonSerializer.Serialize(new{checks=new[]{"shape rail/options","rectangle Shift/palette/selection","single Undo/Redo","cancel","ellipse Shift/Alt","line thickness/angle","precise input","UI save/reopen pixels/style","large worker/Dispatcher","large cancel/Undo","stale result preserves newer transaction"},note="Hidden WPF routes; physical mouse and actual Mac comparison deferred."}));
        }
        finally
        {
            Canvas.CancelInteraction();projectPath=null;
            session.Load(savedDocument);session.ActiveLayerId=savedActive;session.EditMask=savedMask;RestoreTabTools(savedTools);Canvas.Fit();Refresh();
            string full=Path.GetFullPath(root),parent=Path.GetFullPath(Path.GetDirectoryName(reportPath)!).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!full.StartsWith(parent,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("shape-smoke-"))throw new IOException("Unsafe shape cleanup.");
            Directory.Delete(full,true);
        }
    }
}
