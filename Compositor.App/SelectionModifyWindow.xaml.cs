using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Compositor.App;

public enum SelectionModification { Feather, Expand, Contract }
public partial class SelectionModifyWindow : Window
{
    private readonly EditorSession session;
    private readonly Document original;
    private readonly SelectionModification operation;
    private DocumentSelection? result;
    private SKBitmap? bitmap;
    private CancellationTokenSource? pending;
    private bool initialized, closed, finished;
    private long version;
    internal Task PendingPreview { get; private set; } = Task.CompletedTask;
    public SelectionModifyWindow(EditorSession session, SelectionModification operation)
    {
        if (session.InTransaction || session.Document.Selection is not { IsEmpty: false })
            throw new InvalidOperationException("Draw a nonempty selection first.");
        this.session=session;original=session.Document;this.operation=operation;
        InitializeComponent();Title=Heading.Text=$"{operation} selection";
        Amount.Maximum=operation==SelectionModification.Feather?250:500;
        session.Begin();initialized=true;Closed+=(_,_)=>CancelEdit();
        PendingPreview=UpdatePreview(true);
    }
    private void AmountChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(!initialized||closed)return;
        AmountText.Text=Amount.Value.ToString("0",CultureInfo.InvariantCulture);
        PendingPreview=UpdatePreview();
    }
    private void CommitAmount(object sender,RoutedEventArgs e)
    {
        if(double.TryParse(AmountText.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)&&double.IsFinite(n))
            Amount.Value=Math.Clamp(Math.Round(n),0,Amount.Maximum);
        AmountText.Text=Amount.Value.ToString("0",CultureInfo.InvariantCulture);
    }
    private void AmountKeyDown(object sender,KeyEventArgs e)
    {if(e.Key==Key.Enter){CommitAmount(sender,e);e.Handled=true;}}
    internal void SetAmount(double amount)
    {
        Amount.Value=Math.Clamp(Math.Round(amount),0,Amount.Maximum);
        AmountText.Text=Amount.Value.ToString("0",CultureInfo.InvariantCulture);
        PendingPreview=UpdatePreview(true);
    }
    private async Task UpdatePreview(bool immediate=false)
    {
        long current=++version;pending?.Cancel();var cancellation=new CancellationTokenSource();pending=cancellation;
        result=null;ApplyButton.IsEnabled=false;PreviewStatus.Text="Updating selection…";
        double amount=Amount.Value;
        try
        {
            if(!immediate)await Task.Delay(120,cancellation.Token);
            var update=await Task.Run(()=>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var selection=operation==SelectionModification.Feather?SelectionGeometry.Feather(original.Selection!,amount):
                    SelectionGeometry.Resize(original.Selection!,operation==SelectionModification.Expand?amount:-amount,original.Width,original.Height);
                var coverage=SelectionCoverage.Create(selection,original.Width,original.Height);
                cancellation.Token.ThrowIfCancellationRequested();
                const int width=420,height=210;var rgba=new byte[width*height*4];
                double scale=Math.Min((width-12d)/original.Width,(height-12d)/original.Height);
                double ox=(width-original.Width*scale)/2,oy=(height-original.Height*scale)/2;
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    var point=new PointD((x+.5-ox)/scale,(y+.5-oy)/scale);
                    byte gray=point.X<0||point.Y<0||point.X>=original.Width||point.Y>=original.Height?(byte)28:(byte)Math.Round(coverage.Sample(point)*255);
                    int p=(y*width+x)*4;rgba[p]=rgba[p+1]=rgba[p+2]=gray;rgba[p+3]=255;
                }
                return (selection,rgba);
            },cancellation.Token);
            if(closed||current!=version)return;
            result=update.selection;
            bitmap?.Dispose();bitmap=new SKBitmap(new SKImageInfo(420,210,SKColorType.Rgba8888,SKAlphaType.Premul));
            update.rgba.CopyTo(bitmap.GetPixelSpan());Preview.InvalidateVisual();
            Present();ApplyButton.IsEnabled=true;
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested){}
        catch(Exception error)
        {if(!closed&&current==version){session.Preview(original);PreviewStatus.Text=error.Message;}}
        finally{if(ReferenceEquals(pending,cancellation))pending=null;cancellation.Dispose();}
    }
    private void Present()
    {
        if(closed)return;
        session.Preview(result is not null&&ShowPreview.IsChecked==true?original with{Selection=result}:original);
        if(result is not null)PreviewStatus.Text=result.IsEmpty?"Empty selection — painting is blocked.":$"Feather: {result.Feather:0.##} px · Apply creates one Undo step.";
    }
    private void TogglePreview(object sender,RoutedEventArgs e)=>Present();
    internal void SetPreviewVisible(bool visible){ShowPreview.IsChecked=visible;Present();}
    private void PaintPreview(object? sender,SKPaintSurfaceEventArgs e)
    {
        e.Surface.Canvas.Clear(new SKColor(28,30,34));
        if(bitmap is not null)e.Surface.Canvas.DrawBitmap(bitmap,new SKRect(0,0,e.Info.Width,e.Info.Height),new SKSamplingOptions(SKFilterMode.Linear));
    }
    private void ApplyClicked(object sender,RoutedEventArgs e)=>ApplyEdit();
    internal void ApplyEdit()
    {
        if(closed||result is null||!ApplyButton.IsEnabled)return;
        session.Preview(original with{Selection=result});session.Commit();finished=true;Close();
    }
    private void CancelClicked(object sender,RoutedEventArgs e){CancelEdit();Close();}
    internal void CancelEdit()
    {
        if(closed)return;closed=true;++version;pending?.Cancel();
        if(!finished)session.Cancel();bitmap?.Dispose();bitmap=null;
    }
}
