using System.Windows;

namespace Compositor.App;

public partial class MainWindow
{
    private void AddCurves(object sender, RoutedEventArgs e) => OpenCurves(true);
    private void EditCurves(object sender, RoutedEventArgs e) => OpenCurves(false);
    private void OpenCurves(bool create) => Safe(() =>
    {
        var dialog = new CurvesWindow(session, create) { Owner = this };
        try { dialog.ShowDialog(); }
        finally { dialog.CancelEdit(); Refresh(); Canvas.Focus(); }
    });
}
