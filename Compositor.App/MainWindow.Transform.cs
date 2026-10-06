using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Compositor.Core;
namespace Compositor.App;
public partial class MainWindow
{
    private bool updatingTransform;
    private void StartTransform(object? sender,RoutedEventArgs e)=>Safe(()=>
    {ToolPicker.SelectedIndex=0;Canvas.BeginTransform();Canvas.Focus();});
    private void ApplyTransform(object? sender,RoutedEventArgs e)
    {try{Canvas.CommitTransform();Canvas.Focus();}catch(Exception error){ShowError(error.Message);}}
    private void CancelTransform(object? sender,RoutedEventArgs e){Canvas.CancelTransform();Canvas.Focus();}
    private void RefreshTransformControls()
    {
        if(TransformOptions is null)return;
        bool active=Canvas.IsTransforming;
        TransformOptions.Visibility=active?Visibility.Visible:Visibility.Collapsed;
        RefreshToolControls();
        if(Canvas.TransformDraft is not {} t||Canvas.TransformInitial is not {} initial)return;
        updatingTransform=true;
        TransformAngle.Text=F(t.Rotation);TransformAngleSlider.Value=Math.Clamp(t.Rotation,-180,180);
        double scale=t.Width/initial.Width*100;
        TransformScale.Text=F(scale);TransformScaleSlider.Value=Math.Clamp(scale,10,400);
        updatingTransform=false;
    }
    private void SetTransform(Func<LayerTransform,LayerTransform> change)
    {
        if(updatingTransform||Canvas?.TransformDraft is not {} t)return;
        try{Canvas.PreviewTransform(change(t));}
        catch(Exception error){ShowError(error.Message);}
    }
    private void TransformAngleChanged(object sender,RoutedPropertyChangedEventArgs<double> e)=>SetTransform(t=>t with{Rotation=e.NewValue});
    private void TransformScaleChanged(object sender,RoutedPropertyChangedEventArgs<double> e)=>SetTransform(t=>ScaledTransform(t,e.NewValue));
    private LayerTransform ScaledTransform(LayerTransform t,double percent)
    {
        var initial=Canvas.TransformInitial!;
        double w=initial.Width*percent/100,h=initial.Height*percent/100;
        return t with{X=t.X+(t.Width-w)/2,Y=t.Y+(t.Height-h)/2,Width=w,Height=h};
    }
    private void TransformNumberCommit(object sender,RoutedEventArgs e)
    {
        if(updatingTransform||!Canvas.IsTransforming)return;
        var field=(TextBox)sender;
        if (Canvas.TransformDraft is {} current && field.Text == F(field == TransformAngle ? current.Rotation : current.Width / Canvas.TransformInitial!.Width * 100)) return;
        if(!double.TryParse(field.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double number)||!double.IsFinite(number))
        {RefreshTransformControls();return;}
        SetTransform(t=>field==TransformAngle?t with{Rotation=number}:ScaledTransform(t,number));
    }
    private void TransformNumberKey(object sender,System.Windows.Input.KeyEventArgs e)
    {
        if(e.Key!=System.Windows.Input.Key.Enter)return;
        TransformNumberCommit(sender,e);Canvas.Focus();e.Handled=true;
    }
    private void TransformRatioChanged(object sender,RoutedEventArgs e)=>Canvas.TransformLockRatio=TransformRatio.IsChecked==true;
    private void TransformFlipX(object sender,RoutedEventArgs e)=>SetTransform(t=>t with{FlipX=!t.FlipX});
    private void TransformFlipY(object sender,RoutedEventArgs e)=>SetTransform(t=>t with{FlipY=!t.FlipY});
}
