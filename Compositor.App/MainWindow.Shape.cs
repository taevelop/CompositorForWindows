using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Compositor.Core;
namespace Compositor.App;
public partial class MainWindow
{
    private LayerShapeStyle ReadShape()
    {
        ColorPickerWindow.TryHex(BrushColor.Text,out var color);
        double Value(TextBox input,double fallback,double minimum)=>double.TryParse(input.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)&&double.IsFinite(v)?Math.Clamp(v,minimum,5000):fallback;
        return new(ShapeLine.IsChecked==true?ShapeKind.Line:ShapeEllipse.IsChecked==true?ShapeKind.Ellipse:ShapeKind.Rectangle,
            color.R/255d,color.G/255d,color.B/255d,Value(ShapeRadiusValue,ShapeRadius.Value,0),Value(ShapeWidthValue,ShapeWidth.Value,1));
    }
    private void InitializeShapeControls()
    {
        void Link(Slider slider,TextBox input)
        {
            bool updating=false;
            slider.ValueChanged+=(_,_)=>{if(updating)return;input.Text=Math.Round(slider.Value).ToString(CultureInfo.InvariantCulture);};
            input.TextChanged+=(_,_)=>
            {
                if(updating||!double.TryParse(input.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value))return;
                updating=true;slider.Value=Math.Clamp(value,slider.Minimum,slider.Maximum);updating=false;
            };
            void Commit()
            {
                double value=double.TryParse(input.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)&&double.IsFinite(v)?Math.Clamp(v,slider.Minimum,5000):slider.Value;
                input.Text=value.ToString("0.###",CultureInfo.InvariantCulture);
            }
            input.LostKeyboardFocus+=(_,_)=>Commit();
            input.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){Commit();Canvas.Focus();e.Handled=true;}};
        }
        Link(ShapeRadius,ShapeRadiusValue);Link(ShapeWidth,ShapeWidthValue);
    }
    private void ShapeKindChanged(object sender,RoutedEventArgs e)
    {if(Canvas is null||ShapeOptions is null)return;Canvas.CancelInteraction();RefreshShapeControls();}
    private void RefreshShapeControls()
    {
        if(ShapeRadius is null||ShapeWidth is null)return;
        var radius=ShapeRectangle.IsChecked==true?Visibility.Visible:Visibility.Collapsed;
        var width=ShapeLine.IsChecked==true?Visibility.Visible:Visibility.Collapsed;
        ShapeRadius.Visibility=ShapeRadiusLabel.Visibility=ShapeRadiusValue.Visibility=radius;
        ShapeWidth.Visibility=ShapeWidthLabel.Visibility=ShapeWidthValue.Visibility=width;
        ShapeColorButton.IsEnabled=true;
    }
    private void CycleShapeKind()
    {if(ShapeRectangle.IsChecked==true)ShapeEllipse.IsChecked=true;else if(ShapeEllipse.IsChecked==true)ShapeLine.IsChecked=true;else ShapeRectangle.IsChecked=true;}
}
