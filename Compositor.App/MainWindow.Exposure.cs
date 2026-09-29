using System.Windows;

namespace Compositor.App;

public partial class MainWindow
{
    private void AddExposure(object sender, RoutedEventArgs e) => OpenExposure(true);
    private void EditExposure(object sender, RoutedEventArgs e) => OpenExposure(false);
    private void OpenExposure(bool create) => Safe(() =>
    {
        var dialog = new ExposureWindow(session, create) { Owner = this };
        try { dialog.ShowDialog(); }
        finally { dialog.CancelEdit(); Refresh(); Canvas.Focus(); }
    });
}
