using System.Security.Cryptography;

namespace Compositor.Imaging;

public static class BackgroundModelStore
{
    public const string FileName="background.onnx";
    internal static bool KnownHash(string hash)=>hash==U2NetPredictor.ModelSha256||hash=="8D10D2F3BB75AE3B6D527C77944FC5E7DCD94B29809D47A739A7A728A912B491";
    public static string Install(string source,string directory,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        string root=Path.GetFullPath(directory),destination=Path.Combine(root,FileName);
        ProjectStore.CheckNoLinks(root);ProjectStore.CheckNoLinks(destination);Directory.CreateDirectory(root);
        string temporary=Path.Combine(root,".background-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer=new byte[65536];long total=0;int read;
                while((read=input.Read(buffer))!=0)
                {
                    cancellation.ThrowIfCancellationRequested();total+=read;
                    if(total>192L*1024*1024)throw new InvalidDataException("Background model exceeds its supported size.");
                    hash.AppendData(buffer,0,read);output.Write(buffer,0,read);
                }
                if(!KnownHash(Convert.ToHexString(hash.GetHashAndReset())))throw new InvalidDataException("Choose a verified U2Net or U2Netp model. This file does not match the supported release.");
                cancellation.ThrowIfCancellationRequested();output.Flush(true);
            }
            ProjectStore.CheckNoLinks(destination);cancellation.ThrowIfCancellationRequested();
            File.Move(temporary,destination,true);return destination;
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
