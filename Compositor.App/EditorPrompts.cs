using System.Windows;

namespace Compositor.App;

/// <summary>Dialog boundary used by the editor and its hidden integration checks.</summary>
internal sealed record EditorPrompts(Func<MessageBoxResult> ConfirmSaveChanges, Action<string> ShowError)
{
    public static EditorPrompts For(Window owner) => new(
        () => MessageBox.Show(owner, "Save changes before continuing?", "Unsaved project", MessageBoxButton.YesNoCancel, MessageBoxImage.Question),
        message => MessageBox.Show(owner, message, "Compositor", MessageBoxButton.OK, MessageBoxImage.Warning));
}
