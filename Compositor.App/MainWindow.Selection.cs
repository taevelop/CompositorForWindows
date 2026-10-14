using System.Windows;
using System.Windows.Controls;
using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;

public partial class MainWindow
{
    private async void SelectSubject(object sender,RoutedEventArgs e)
    {
        if(!await ResolveGradientBeforeAction())return;
        if(!busy&&Canvas.IsTransforming)Canvas.CommitTransform();if(busy||session.InTransaction)return;
        string directory=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CompositorForWindows","Models");
        string path=System.IO.Path.Combine(directory,BackgroundModelStore.FileName);
        if(!System.IO.File.Exists(path)){var setup=new BackgroundModelSetupWindow(directory){Owner=this};if(setup.ShowDialog()!=true)return;}
        var mode=sender is MenuItem {Tag:string tag}&&Enum.TryParse<Compositor.Core.SelectionMode>(tag,out var selected)?selected:Compositor.Core.SelectionMode.Replace;
        var target=session;var original=target.Document;Document? result=null;
        if(!await Work("Selecting foreground subject…",()=>{using var predictor=new U2NetPredictor(path);result=SubjectSelection.Compute(original,predictor.Predict,mode);}))return;
        if(ReferenceEquals(session,target)&&ReferenceEquals(target.Document,original)&&!target.InTransaction&&result is not null)target.Apply(_=>result);
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

