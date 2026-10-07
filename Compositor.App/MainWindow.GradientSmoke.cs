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
        var anchor=new PointD(12,-8);
        foreach(double angle in new[]{0.1,0.7,1.6,2.3,3.0,-0.7,-1.6,-2.3})
        {
            var snapped=EditorCanvas.SnapGradientPoint(new(anchor.X+10*Math.Cos(angle),anchor.Y+10*Math.Sin(angle)),anchor);
            double resultAngle=Math.Atan2(snapped.Y-anchor.Y,snapped.X-anchor.X)/(Math.PI/4);
            if(Math.Abs(double.Hypot(snapped.X-anchor.X,snapped.Y-anchor.Y)-10)>1e-9||Math.Abs(resultAngle-Math.Round(resultAngle))>1e-9)
                throw new InvalidOperationException("Gradient Shift snapping changed length or missed the 45-degree grid.");
        }
        var original=session.Document;var tools=CaptureTabTools();
        try
        {
            var doc=Document.Create(3,1);session.Load(doc);ToolPicker.SelectedIndex=10;Canvas.RestoreView(20,30,30);
            BrushColor.Text="#FF0000";SetBackgroundColor("#0000FF");GradientTransparent.IsChecked=false;GradientOpacity.Value=100;
            if(Canvas.Tool!=EditorTool.Gradient||ToolTitle.Text!="Gradient"||GradientOptions.Visibility!=Visibility.Visible)throw new InvalidOperationException("Gradient tool UI failed.");
            Canvas.BeginInteraction(MouseButton.Left,new(40,40));Canvas.MoveInteraction(new(80,40));Canvas.FinishInteraction(MouseButton.Left,new(80,40));
            if(!Canvas.IsGradientPreparing)throw new InvalidOperationException("Gradient preparation state was not exposed.");
            using(var bitmap=new SkiaSharp.SKBitmap(320,60))
            using(var canvas=new SkiaSharp.SKCanvas(bitmap))
            {
                canvas.Clear(SkiaSharp.SKColors.Transparent);Canvas.DrawGradientStatus(canvas);
                using var image=SkiaSharp.SKImage.FromBitmap(bitmap);using var png=image.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);
                Directory.CreateDirectory("artifacts");File.WriteAllBytes(Path.Combine("artifacts","gradient-status.png"),png.ToArray());
            }
            await Canvas.GradientPending;
            if(Canvas.IsGradientPreparing)throw new InvalidOperationException("Completed gradient remained busy.");
            if(!Canvas.HasGradient||session.UndoCount!=0||!session.Document.Layers[0].Pixels.ToRgba().SequenceEqual(new byte[]{255,0,0,255,128,0,128,255,0,0,255,255}))throw new InvalidOperationException("Gradient drag preview failed.");
            Canvas.BeginInteraction(MouseButton.Left,new(80,40));Canvas.MoveInteraction(new(120,40));Canvas.FinishInteraction(MouseButton.Left,new(120,40));await Canvas.GradientPending;
            if(session.Document.Layers[0].Pixels.ToRgba()[8]!=128)throw new InvalidOperationException("Gradient endpoint handle failed.");
            await Canvas.CommitGradientAsync();if(session.UndoCount!=1||Canvas.HasGradient)throw new InvalidOperationException("Gradient commit failed.");
            session.Undo();if(!ReferenceEquals(doc,session.Document))throw new InvalidOperationException("Gradient Undo failed.");
            Canvas.BeginInteraction(MouseButton.Left,new(40,40));Canvas.MoveInteraction(new(80,40));Canvas.FinishInteraction(MouseButton.Left,new(80,40));Canvas.CancelGradient();await Canvas.GradientPending;
            if(!ReferenceEquals(doc,session.Document)||session.InTransaction)throw new InvalidOperationException("Gradient cancel failed.");
            await GradientSaveSmokeTest();
            await GradientTargetSmokeTest();
            await GradientPaletteSmokeTest();
            await GradientTabSmokeTest();
            var shortcutDoc=Document.Create(3,1);session.Load(shortcutDoc);ToolPicker.SelectedIndex=10;Canvas.RestoreView(20,30,30);
            Canvas.BeginInteraction(MouseButton.Left,new(40,40));Canvas.FinishInteraction(MouseButton.Left,new(80,40));
            if(!await HandleGradientShortcut(Key.G,ModifierKeys.None)||!Canvas.HasGradient||session.UndoCount!=0)
                throw new InvalidOperationException("Current gradient tool shortcut unexpectedly committed the edit.");
            if(!await HandleGradientShortcut(Key.D3,ModifierKeys.None,1)||GradientOpacity.Value!=30||GradientOpacityValue.Text!="30")
                throw new InvalidOperationException("Gradient opacity shortcut failed.");
            await Canvas.GradientPending;
            if(session.Document.Layers[0].Pixels.ToRgba()[3]!=77||session.UndoCount!=0)
                throw new InvalidOperationException("Opacity shortcut did not update pending alpha without committing.");
            if(await HandleGradientShortcut(Key.D0,ModifierKeys.Control)||GradientOpacity.Value!=30)
                throw new InvalidOperationException("Modified opacity key was intercepted.");
            await HandleGradientShortcut(Key.NumPad0,ModifierKeys.None,2);await Canvas.GradientPending;
            if(GradientOpacity.Value!=100)throw new InvalidOperationException("Numpad zero did not restore full opacity.");
            await HandleGradientShortcut(Key.NumPad5,ModifierKeys.None,2.2);
            if(GradientOpacity.Value!=5)throw new InvalidOperationException("Two-digit leading zero opacity failed.");
            await HandleGradientShortcut(Key.D4,ModifierKeys.None,3);await HandleGradientShortcut(Key.D5,ModifierKeys.None,3.2);
            if(GradientOpacity.Value!=45)throw new InvalidOperationException("Two-digit opacity failed.");
            await HandleGradientShortcut(Key.D0,ModifierKeys.None,4);await Canvas.GradientPending;
            if(await HandleGradientShortcut(Key.B,ModifierKeys.Control)||!Canvas.HasGradient)
                throw new InvalidOperationException("Unrelated modified shortcut changed the gradient.");
            if(!await HandleGradientShortcut(Key.B,ModifierKeys.None)||Canvas.HasGradient||Canvas.Tool!=EditorTool.Brush||session.UndoCount!=1)
                throw new InvalidOperationException("Brush shortcut did not commit the pending gradient.");
            session.Undo();if(!ReferenceEquals(shortcutDoc,session.Document))throw new InvalidOperationException("Tool shortcut gradient Undo failed.");
        }
        finally{Canvas.CancelGradient();session.Load(original);GradientTransparent.IsChecked=true;GradientOpacity.Value=100;RestoreTabTools(tools);Canvas.Fit();Refresh();}
    }
    private async Task GradientPaletteSmokeTest()
    {
        var doc=Document.Create(3,1);session.Load(doc);Refresh();ToolPicker.SelectedIndex=10;Canvas.RestoreView(20,30,30);
        GradientLinear.IsChecked=true;GradientTransparent.IsChecked=false;GradientReverse.IsChecked=false;GradientOpacity.Value=100;
        BrushColor.Text="#FF0000";SetBackgroundColor("#0000FF");
        Canvas.BeginInteraction(MouseButton.Left,new(40,40));Canvas.FinishInteraction(MouseButton.Left,new(80,40));
        BrushColor.Text="#00FF00";SetBackgroundColor("#FFFFFF");await Canvas.GradientPending;
        if(!session.Document.Layers[0].Pixels.ToRgba().SequenceEqual(new byte[]{0,255,0,255,128,255,128,255,255,255,255,255})||session.UndoCount!=0)
            throw new InvalidOperationException("Pending gradient palette did not replace the original preview.");
        var brush=(System.Windows.Media.LinearGradientBrush)GradientSwatch.Background;
        if(brush.GradientStops[0].Color!=System.Windows.Media.Colors.Lime||brush.GradientStops[1].Color!=System.Windows.Media.Colors.White)
            throw new InvalidOperationException("Gradient swatch palette mismatch.");
        SwapPalette(null,new());await Canvas.GradientPending;
        if(session.Document.Layers[0].Pixels.ToRgba()[0]!=255||session.Document.Layers[0].Pixels.ToRgba()[8]!=0)
            throw new InvalidOperationException("Pending gradient palette swap failed.");
        GradientTransparent.IsChecked=true;GradientReverse.IsChecked=true;GradientChanged(this,new());await Canvas.GradientPending;
        brush=(System.Windows.Media.LinearGradientBrush)GradientSwatch.Background;
        if(brush.GradientStops[0].Color.A!=0||brush.GradientStops[1].Color.A!=255||session.Document.Layers[0].Pixels.ToRgba()[3]!=0)
            throw new InvalidOperationException("Transparent reversed gradient swatch mismatch.");
        Canvas.CancelGradient();if(!ReferenceEquals(doc,session.Document)||session.UndoCount!=0)
            throw new InvalidOperationException("Palette-edited gradient cancellation lost the original document.");
        GradientTransparent.IsChecked=false;GradientReverse.IsChecked=false;
    }
    private async Task GradientTabSmokeTest()
    {
        var original=workspace.Current;var savedPrompts=prompts;RememberTab();
        var tab=workspace.New(Document.Create(3,1));BindCurrentTab();
        try
        {
            ToolPicker.SelectedIndex=10;Canvas.RestoreView(20,30,30);
            BrushColor.Text="#FF0000";SetBackgroundColor("#0000FF");GradientTransparent.IsChecked=false;
            Canvas.BeginInteraction(MouseButton.Left,new(40,40));Canvas.FinishInteraction(MouseButton.Left,new(80,40));
            await SelectTabAsync(original.Id);
            if(workspace.Current!=original||tab.Session.InTransaction||tab.Session.UndoCount!=1||Canvas.HasGradient||tab.Session.Document.Layers[0].Pixels.ToRgba()[0]!=255)
                throw new InvalidOperationException("Tab switch lost the pending gradient.");
            await SelectTabAsync(tab.Id);
            Canvas.RestoreView(20,30,30);GradientReverse.IsChecked=true;
            Canvas.BeginInteraction(MouseButton.Left,new(40,40));Canvas.FinishInteraction(MouseButton.Left,new(80,40));
            prompts=new(()=>MessageBoxResult.Cancel,message=>throw new InvalidOperationException(message));
            await CloseTab(tab.Id);
            if(workspace.Current!=tab||tab.Session.InTransaction||tab.Session.UndoCount!=2||!tab.Session.IsModified||Canvas.HasGradient)
                throw new InvalidOperationException("Cancelled tab close did not retain the committed gradient.");
            GradientReverse.IsChecked=false;
            Canvas.BeginInteraction(MouseButton.Left,new(40,40));Canvas.FinishInteraction(MouseButton.Left,new(80,40));
            await HandleDocumentShortcut(Key.Tab,ModifierKeys.Control);
            if(workspace.Current==tab||tab.Session.InTransaction||tab.Session.UndoCount!=3)
                throw new InvalidOperationException("Ctrl+Tab did not commit the gradient.");
            await SelectTabAsync(tab.Id);
            GradientRadial.IsChecked=true;GradientTransparent.IsChecked=false;GradientReverse.IsChecked=true;GradientOpacity.Value=37;
            await SelectTabAsync(original.Id);
            GradientLinear.IsChecked=true;GradientTransparent.IsChecked=true;GradientReverse.IsChecked=false;GradientOpacity.Value=82;
            await SelectTabAsync(tab.Id);
            if(GradientRadial.IsChecked!=true||GradientLinear.IsChecked==true||GradientTransparent.IsChecked!=false||GradientReverse.IsChecked!=true||GradientOpacity.Value!=37||GradientOpacityValue.Text!="37")
                throw new InvalidOperationException("Radial gradient tab settings were not restored.");
            await SelectTabAsync(original.Id);
            if(GradientLinear.IsChecked!=true||GradientRadial.IsChecked==true||GradientTransparent.IsChecked!=true||GradientReverse.IsChecked!=false||GradientOpacity.Value!=82||GradientOpacityValue.Text!="82")
                throw new InvalidOperationException("Linear gradient tab settings were not restored.");
        }
        finally
        {
            Canvas.CancelGradient();prompts=savedPrompts;workspace.Close(tab.Id,true);tabViews.Remove(tab.Id);
            workspace.Select(original.Id);BindCurrentTab();GradientReverse.IsChecked=false;
        }
    }
    private async Task GradientTargetSmokeTest()
    {
        var doc=Document.Create(3,1);var first=doc.Layers[0] with{Mask=LayerMask.Solid(3,1)};
        var second=Layer.Blank("Second",3,1);doc=doc.Replace(first) with{Layers=doc.Replace(first).Layers.Add(second)};
        session.Load(doc,first.Id);Refresh();ToolPicker.SelectedIndex=10;Canvas.RestoreView(20,30,30);
        BrushColor.Text="#FF0000";SetBackgroundColor("#0000FF");GradientTransparent.IsChecked=false;
        Canvas.BeginInteraction(MouseButton.Left,new(40,40));Canvas.FinishInteraction(MouseButton.Left,new(80,40));
        EditTarget.SelectedIndex=1;
        await maskTargetChange;
        if(!session.EditMask||session.InTransaction||Canvas.HasGradient||session.UndoCount!=1||EditTarget.SelectedIndex!=1)
            throw new InvalidOperationException("Gradient image-to-mask target change failed.");
        MaskSlider.Value=0;
        Canvas.BeginInteraction(MouseButton.Left,new(40,40));Canvas.FinishInteraction(MouseButton.Left,new(80,40));
        Layers.SelectedItem=Layers.Items.Cast<LayerRow>().Single(row=>row.Id==second.Id);
        await layerSelectionChange;
        if(session.ActiveLayerId!=second.Id||session.InTransaction||Canvas.HasGradient||session.UndoCount!=2||
            session.Document.Layers[0].Mask!.Pixels.ToRgba()[0]!=0)
            throw new InvalidOperationException($"Gradient mask-to-layer target change failed: active={session.ActiveLayerId==second.Id}, transaction={session.InTransaction}, gradient={Canvas.HasGradient}, undo={session.UndoCount}, mask={session.Document.Layers[0].Mask!.Pixels.ToRgba()[0]}.");
        session.Undo();
        if(session.Document.Layers[0].Mask!.Pixels.ToRgba()[0]!=255||session.Document.Layers[0].Pixels.ToRgba()[0]!=255)
            throw new InvalidOperationException("Gradient target changes did not preserve separate Undo steps.");
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
            if(!await HandleGradientShortcut(Key.S,ModifierKeys.Control)||Canvas.HasGradient||session.InTransaction||session.UndoCount!=1||session.IsModified)
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
