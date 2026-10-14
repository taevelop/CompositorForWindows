using System.Windows;
namespace Compositor.App;
public partial class MainWindow
{
    private void SmearModeChanged(object sender, RoutedEventArgs e)
    {
        if (Canvas is null || !IsLoaded) return;
        Canvas.CancelInteraction();RefreshToolControls();UpdateStatus();
    }
}
