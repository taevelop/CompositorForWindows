using System.Windows;

namespace Compositor.App;

public partial class MainWindow
{
    private void AddLevels(object sender, RoutedEventArgs e) => OpenLevels(true);
    private void EditLevels(object sender, RoutedEventArgs e) => OpenLevels(false);
    private void OpenLevels(bool create) => Safe(() =>
    {
        var dialog = new LevelsWindow(session, create) { Owner = this };
        try { dialog.ShowDialog(); }
        finally { dialog.CancelEdit(); Refresh(); Canvas.Focus(); }
    });
}
