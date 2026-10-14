using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;

public class BackgroundModelStoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedDownloadKeepsInstalledModelAndRemovesTemporaryFiles(bool oversized)
    {
        string root=Path.Combine(Path.GetTempPath(),"compositor-download-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string target=Path.Combine(root,BackgroundModelStore.FileName);File.WriteAllBytes(target,[8,9]);
        try
        {
            using var client=new HttpClient(new ReplyHandler(oversized));
            await Assert.ThrowsAsync<InvalidDataException>(()=>BackgroundModelStore.DownloadAsync(root,client,true));
            Assert.Equal(new byte[]{8,9},File.ReadAllBytes(target));Assert.Empty(Directory.GetFiles(root,"*.tmp"));
        }
        finally{File.Delete(target);Directory.Delete(root);}
    }
    private sealed class ReplyHandler(bool oversized):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Assert.Equal("https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx",request.RequestUri!.AbsoluteUri);
            var response=new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new ByteArrayContent([1,2,3])};
            if(oversized)response.Content.Headers.ContentLength=193L*1024*1024;
            return Task.FromResult(response);
        }
    }

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
