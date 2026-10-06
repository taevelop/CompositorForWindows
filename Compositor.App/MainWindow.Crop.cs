using System.Windows;
using System.Windows.Controls;
namespace Compositor.App;
public partial class MainWindow
{
    private void RefreshCropControls()
    {
        if(Canvas?.Session is null || CropApplyButton is null)return;
        var frame=Canvas.CropDraft;
        CropApplyButton.IsEnabled=frame is not null;
        CropSizeLabel.Text=frame is {} f?$"{f.Width:N0} × {f.Height:N0} px":"Drag to crop";
    }
    private void CropRatioChanged(object sender, SelectionChangedEventArgs e)
    {
        if(Canvas?.Session is null)return;
        Safe(()=>Canvas.ChangeCropRatio((CropRatioPicker.SelectedItem as ComboBoxItem)?.Content?.ToString()??"Free"));
    }
    private void CropSnapChanged(object sender,RoutedEventArgs e)
    {if(Canvas is not null)Canvas.CropSnapEnabled=CropSnap.IsChecked==true;}
    private void ApplyCrop(object? sender,RoutedEventArgs e)=>Safe(()=>{Canvas.CommitCrop();Canvas.Focus();});
    private void CancelCrop(object? sender,RoutedEventArgs e){Canvas.CancelInteraction();Canvas.CancelCrop();Canvas.Focus();}
}
