using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;

public class BackgroundModelStoreTests
{
    [Fact]
    public void InvalidOrCancelledInstallKeepsPreviousFileAndLeavesNoTemporaryFile()
    {
        string root=Path.Combine(Path.GetTempPath(),"compositor-model-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string source=Path.Combine(root,"input.onnx"),target=Path.Combine(root,BackgroundModelStore.FileName);
        try
        {
            File.WriteAllBytes(source,[1,2,3]);File.WriteAllBytes(target,[8,9]);
            Assert.Throws<InvalidDataException>(()=>BackgroundModelStore.Install(source,root));Assert.Equal(new byte[]{8,9},File.ReadAllBytes(target));
            Assert.Throws<OperationCanceledException>(()=>BackgroundModelStore.Install(source,root,new CancellationToken(true)));
            Assert.Equal(new byte[]{8,9},File.ReadAllBytes(target));Assert.Empty(Directory.GetFiles(root,".background-*.tmp"));
        }
        finally{File.Delete(source);File.Delete(target);Directory.Delete(root);}
    }
}
