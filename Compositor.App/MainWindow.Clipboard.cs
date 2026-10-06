using System.Windows;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    private WindowsPixelClipboard pixelClipboard=new();
    private void CopyPixels(object? sender,RoutedEventArgs e)=>Safe(()=>CopyPixelsCore(false));
    private void CopyMergedPixels(object? sender,RoutedEventArgs e)=>Safe(()=>CopyPixelsCore(true));
    private void CopyPixelsCore(bool merged)
    {
        var content=SelectionClipboardPixels.Copy(session.Document,session.ActiveLayerId,session.EditMask,merged);
        if(content is not null)pixelClipboard.Write(content);
    }
    private void CutPixels(object? sender,RoutedEventArgs e)=>Safe(()=>SelectionClipboardPixels.Cut(session,pixelClipboard.Write));
    private void PastePixels(object? sender,RoutedEventArgs e)=>Safe(()=>
    {
        var value=pixelClipboard.Read();
        if(value is { } copied)SelectionClipboardPixels.Paste(session,copied.Content,copied.PreserveOrigin);
        Canvas.Focus();
    });
}
