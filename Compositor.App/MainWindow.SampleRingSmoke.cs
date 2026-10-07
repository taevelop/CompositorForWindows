using System.Windows;
using System.Windows.Input;
using SkiaSharp;
namespace Compositor.App;
public partial class MainWindow
{
    private void SampleRingSmokeTest()
    {
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        ToolPicker.SelectedIndex=9;Canvas.RestoreView(2,100,100);BrushColor.Text="#123456";
        SampleRingToggle.IsChecked=true;SampleRingChanged(SampleRingToggle,new RoutedEventArgs());
        Canvas.BeginInteraction(MouseButton.Left,new(100,100));
        Canvas.MoveInteraction(new(102,100));
        Check(Canvas.SampleRingColors.Original==new Compositor.Imaging.SampledColor(18,52,86)
            &&Canvas.SampleRingColors.Current==new Compositor.Imaging.SampledColor(0,255,0),"Comparison ring lost original/current colors.");
        using var bitmap=new SKBitmap(220,220);using var drawing=new SKCanvas(bitmap);
        void Draw(){drawing.Clear(SKColors.Transparent);Canvas.DrawSampleRing(drawing);drawing.Flush();}
        Draw();
        Check(bitmap.GetPixel(102,57)==SKColors.Lime&&bitmap.GetPixel(102,143)==new SKColor(18,52,86),"Comparison ring halves or screen radius differ.");
        Check(bitmap.GetPixel(102,47)==new SKColor(115,115,115)&&bitmap.GetPixel(102,100).Alpha==0,"Comparison ring outline or hollow center differs.");
        var output=System.IO.Path.Combine(AppContext.BaseDirectory,"sample-ring.png");
        using(var data=bitmap.Encode(SKEncodedImageFormat.Png,100))using(var stream=System.IO.File.Create(output))data.SaveTo(stream);
        SampleRingToggle.IsChecked=false;SampleRingChanged(SampleRingToggle,new RoutedEventArgs());Draw();
        Check(bitmap.GetPixel(102,57).Alpha==0,"Disabled comparison ring remains visible.");
        var saved=CaptureTabTools();Canvas.ShowSampleRing=true;RestoreTabTools(saved);
        Check(!Canvas.ShowSampleRing&&SampleRingToggle.IsChecked==false,"Comparison ring preference was not restored.");
        Canvas.ShowSampleRing=true;Canvas.CancelInteraction();Draw();
        Check(bitmap.GetPixel(102,57).Alpha==0,"Comparison ring remained after cancellation.");
    }
}
