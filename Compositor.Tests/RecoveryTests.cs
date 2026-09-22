using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;

public sealed class RecoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "CompositorRecovery-" + Guid.NewGuid().ToString("N"));
    public RecoveryTests() => Directory.CreateDirectory(root);
    public void Dispose()
    {
        if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("CompositorRecovery-")) throw new IOException("Unsafe test cleanup path.");
        Directory.Delete(root, true);
    }
    [Fact]
    public void RecoveryOpensAsUnsavedCopyWithoutModifyingEitherDiskVersion()
    {
        var original = Document.Create(64, 64); string path = Path.Combine(root, "Drawing.comp");
        ProjectStore.Save(original, original.Layers[0].Id, path);
        // Filesystem state left by an interrupted publish after the old directory was renamed.
        Directory.Move(path, path + ".recovery");
        var recovered = ProjectStore.LoadRecovery(path + ".recovery");
        var session = new EditorSession(Document.Create(16, 16)); session.Load(recovered.Document, recovered.ActiveLayerId, recovered: true);
        Assert.True(session.IsModified); Assert.False(session.CanUndo);
        byte[] metadata = File.ReadAllBytes(Path.Combine(path + ".recovery", "manifest.json"));
        Assert.Throws<IOException>(() => ProjectStore.Save(session.Document, session.ActiveLayerId, path));
        string destination = Path.Combine(root, "Recovered.comp"); ProjectStore.Save(session.Document, session.ActiveLayerId, destination); session.MarkSaved();
        Assert.False(session.IsModified); Assert.Equal(original.Id, ProjectStore.Load(destination).Document.Id);
        Assert.Equal(metadata, File.ReadAllBytes(Path.Combine(path + ".recovery", "manifest.json"))); Assert.False(Directory.Exists(path));
    }
    [Fact]
    public void ExistingProjectAndRecoveryCopyAreBothPreserved()
    {
        string path = Path.Combine(root, "Drawing.comp"); var doc = Document.Create(32, 32);
        ProjectStore.Save(doc, null, path); ProjectStore.Save(doc, null, Path.Combine(root, "Old.comp"));
        Directory.Move(Path.Combine(root, "Old.comp"), path + ".recovery");
        Assert.Throws<IOException>(() => ProjectStore.Save(doc, null, path));
        Assert.Equal(doc.Id, ProjectStore.LoadRecovery(path + ".recovery").Document.Id);
        Assert.Equal(doc.Id, ProjectStore.Load(path).Document.Id);
    }
    [Fact]
    public void ExclusiveWriterLockPreventsConcurrentSaveWithoutChangingSession()
    {
        string path = Path.Combine(root, "Locked.comp"); var doc = Document.Create(32, 32);
        ProjectStore.Save(doc, null, path); var session = new EditorSession(doc);
        session.Apply(d => d.Replace(d.Layers[0] with { Name = "Pending" }));
        using var handle = new FileStream(path + ".write-lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<IOException>(() => ProjectStore.Save(session.Document, null, path));
        Assert.True(session.IsModified); Assert.Equal("Layer 1", ProjectStore.Load(path).Document.Layers[0].Name);
        Assert.Empty(Directory.GetDirectories(root, "*.staging-*"));
    }
}
