using System.Windows;
using System.Windows.Controls;
using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;

public partial class MainWindow
{
    private CancellationTokenSource? subjectCancellation;
    private Func<Raster,CancellationToken,LayerMask>? subjectPredictorOverride;
    private Task subjectPending=Task.CompletedTask;
    private async void ThumbnailClicked(object sender,RoutedEventArgs e)
    {
        if(sender is not Button {DataContext:LayerRow row,Tag:string tag})return;
        await HandleThumbnail(row.Id,tag=="Mask",System.Windows.Input.Keyboard.Modifiers);e.Handled=true;
    }
    private async Task HandleThumbnail(Guid id,bool mask,System.Windows.Input.ModifierKeys keys)
    {
        if(!await ResolveGradientBeforeAction())return;
        if(!busy&&Canvas.IsTransforming)Canvas.CommitTransform();if(busy||session.InTransaction)return;
        if(keys.HasFlag(System.Windows.Input.ModifierKeys.Control))
        {
            var mode=keys.HasFlag(System.Windows.Input.ModifierKeys.Alt)?Compositor.Core.SelectionMode.Subtract:keys.HasFlag(System.Windows.Input.ModifierKeys.Shift)?Compositor.Core.SelectionMode.Add:Compositor.Core.SelectionMode.Replace;
            subjectPending=RunSelectionAsync("Loading thumbnail selection…",(document,token)=>MaskTracing.LoadSelection(document,id,mask,mode,token));await subjectPending;
        }
        else if(mask||!keys.HasFlag(System.Windows.Input.ModifierKeys.Shift)&&!keys.HasFlag(System.Windows.Input.ModifierKeys.Alt)){session.SelectLayers(new[]{id},id);session.EditMask=mask;Refresh();}
    }
    private async void SelectSubject(object sender,RoutedEventArgs e)
    {
        if(!await ResolveGradientBeforeAction())return;
        if(!busy&&Canvas.IsTransforming)Canvas.CommitTransform();if(busy||session.InTransaction)return;
        string directory=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CompositorForWindows","Models");
        string path=System.IO.Path.Combine(directory,BackgroundModelStore.FileName);
        if(subjectPredictorOverride is null&&!System.IO.File.Exists(path)){var setup=new BackgroundModelSetupWindow(directory){Owner=this};if(setup.ShowDialog()!=true)return;}
        var mode=sender is MenuItem {Tag:string tag}&&Enum.TryParse<Compositor.Core.SelectionMode>(tag,out var selected)?selected:Compositor.Core.SelectionMode.Replace;
        LayerMask Predict(Raster pixels,CancellationToken token){using var predictor=new U2NetPredictor(path);return predictor.Predict(pixels,token);}
        subjectPending=SelectSubjectAsync(mode,subjectPredictorOverride??Predict);await subjectPending;
    }
    private async Task SelectSubjectAsync(Compositor.Core.SelectionMode mode,Func<Raster,CancellationToken,LayerMask> predict)
        =>await RunSelectionAsync("Selecting foreground subject…",(document,token)=>SubjectSelection.Compute(document,predict,mode,token));
    private async void LoadPixelSelection(object sender,RoutedEventArgs e)
    {
        if(!await ResolveGradientBeforeAction())return;
        if(!busy&&Canvas.IsTransforming)Canvas.CommitTransform();if(busy||session.InTransaction)return;
        if(sender is not MenuItem {Tag:string tag})return;var parts=tag.Split(':');
        if(parts.Length!=2||!Enum.TryParse<Compositor.Core.SelectionMode>(parts[1],out var mode))return;
        if(session.ActiveLayerId is not {} id)return;bool mask=parts[0]=="Mask";
        subjectPending=RunSelectionAsync(mask?"Loading hidden mask areas…":"Loading layer pixels…",(document,token)=>MaskTracing.LoadSelection(document,id,mask,mode,token));await subjectPending;
    }
    private async Task RunSelectionAsync(string label,Func<Document,CancellationToken,Document> compute)
    {
        if(busy||session.InTransaction)return;
        var target=session;var original=target.Document;
        using var owner=new CancellationTokenSource();subjectCancellation=owner;busy=true;Editor.IsEnabled=false;
        FillCancelButton.Content="Cancel selection";FillCancelButton.Visibility=Visibility.Visible;Status.Text=label;
        try
        {
            var result=await Task.Run(()=>compute(original,owner.Token));owner.Token.ThrowIfCancellationRequested();
            if(ReferenceEquals(session,target)&&ReferenceEquals(target.Document,original)&&!target.InTransaction)target.Apply(_=>result);
        }
        catch(OperationCanceledException) when(owner.IsCancellationRequested){}
        catch(Exception error){ShowError(error.Message);}
        finally{subjectCancellation=null;busy=false;Editor.IsEnabled=true;FillCancelButton.Content="Cancel fill";FillCancelButton.Visibility=Visibility.Collapsed;Refresh();Canvas.Focus();}
    }
    private void SelectionCenterChanged(object sender, RoutedEventArgs e)
    { Canvas.CancelInteraction(); Canvas.SelectionFromCenter = SelectionCenter.IsChecked == true; }
    private void NudgeSelectedPixels(System.Windows.Input.Key key, int distance) => Safe(() =>
    {
        using var move = SelectionPixelMoveEdit.Begin(session);
        if (move is null) return;
        double dx = key == System.Windows.Input.Key.Left ? -distance : key == System.Windows.Input.Key.Right ? distance : 0;
        double dy = key == System.Windows.Input.Key.Up ? -distance : key == System.Windows.Input.Key.Down ? distance : 0;
        move.Preview(dx, dy); move.Complete(); Canvas.InvalidateVisual();
    });
    private void NudgeSelection(System.Windows.Input.Key key, int distance) => Safe(() =>
    {
        if (session.Document.Selection is not { IsEmpty: false } selected) return;
        double dx = key == System.Windows.Input.Key.Left ? -distance : key == System.Windows.Input.Key.Right ? distance : 0;
        double dy = key == System.Windows.Input.Key.Up ? -distance : key == System.Windows.Input.Key.Down ? distance : 0;
        session.Apply(d => d with { Selection = SelectionGeometry.Move(selected, dx, dy) });
    });
    private void ModifySelection(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (sender is not MenuItem { Tag: string tag } || !Enum.TryParse<SelectionModification>(tag, out var operation)) return;
        var dialog = new SelectionModifyWindow(session, operation) { Owner = this };
        dialog.ShowDialog(); Canvas.Focus();
    });
    private void SelectionModeChanged(object sender, RoutedEventArgs e)
    {
        if (Canvas is not null && sender is RadioButton { Tag: string tag })
        { Canvas.CancelInteraction(); Canvas.SelectionMode = (Compositor.Core.SelectionMode)int.Parse(tag); }
    }
    private void SelectionAAChanged(object sender, RoutedEventArgs e)
    { Canvas.CancelInteraction(); Canvas.SelectionAntialiased = SelectionAA.IsChecked == true; }
    private void SelectAll(object? sender, RoutedEventArgs e) => Safe(() =>
        session.Apply(d => d with { Selection = SelectionGeometry.Box(0, 0, d.Width, d.Height) }));
    private void Deselect(object? sender, RoutedEventArgs e) => Safe(() =>
        session.Apply(d => d with { Selection = null }));
    private void InvertSelection(object? sender, RoutedEventArgs e) => Safe(() =>
        session.Apply(d => d with { Selection = d.Selection is { } selected ? SelectionGeometry.Invert(selected, d.Width, d.Height) : SelectionGeometry.Box(0, 0, d.Width, d.Height) }));
    private void ClearSelectionPixels(object? sender, RoutedEventArgs e) => Safe(() =>
    {
        if (session.Document.Selection is not { } selected || session.ActiveLayer is not { IsGroup: false } layer) return;
        var coverage = SelectionCoverage.Create(selected, session.Document.Width, session.Document.Height);
        if (coverage.IsEmpty) return;
        Layer updated;
        if (session.EditMask)
        {
            if (layer.Mask is not { Enabled: true } mask) throw new InvalidOperationException("Select an enabled mask to clear coverage.");
            var source = mask.EditingPixels(layer.Pixels.Width, layer.Pixels.Height);
            var result = SelectionPixels.Blend(source, LayerMask.Solid(source.Width, source.Height, 0).Pixels, mask.Placement??layer.Transform, coverage);
            if (ReferenceEquals(source, result)) return;
            updated = layer with { Mask = mask.WithPixels(result) };
        }
        else
        {
            if (layer.IsAdjustment) throw new InvalidOperationException("Adjustment layers have no image pixels. Select Edit mask.");
            updated = layer with { Pixels = SelectionPixels.Blend(layer.Pixels, new(layer.Pixels.Width, layer.Pixels.Height), layer.Transform, coverage) };
        }
        var next = session.Document.Replace(updated);
        if (EditorSession.UndoBytesRequired(session.Document, next) > EditorSession.MaxHistoryBytes)
            throw new InvalidOperationException("This change exceeds the 256 MiB Undo limit.");
        session.Apply(_ => next);
    });
}



