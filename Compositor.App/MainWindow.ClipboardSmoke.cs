using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    private void ClipboardSmokeTest(string path)
    {
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        IDataObject? stored=null;uint sequence=10;bool reject=false;
        var previous=pixelClipboard;
        pixelClipboard=new WindowsPixelClipboard(()=>stored,data=>{if(reject)throw new IOException("Injected clipboard failure");stored=data;++sequence;},()=>sequence);
        try
        {
            var doc=Document.Create(10,10);
            var source=doc.Layers[0] with{Pixels=Raster.FromRgba(2,1,[80,20,10,128,30,40,50,100]),Transform=new(3,4,2,1)};
            doc=doc.Replace(source) with{Selection=SelectionGeometry.Box(3,4,2,1,false,false)};
            session.Load(doc);CopyPixels(null,new());
            Check(stored!.GetDataPresent("PNG",false)&&stored.GetDataPresent(DataFormats.Bitmap),"Clipboard did not supply PNG and bitmap.");
            var internalCopy=pixelClipboard.Read()!.Value;
            Check(internalCopy.PreserveOrigin&&internalCopy.Content.Origin==new PointD(3,4),"Internal clipboard lost position.");
            PastePixels(null,new());Check(session.ActiveLayer!.Transform.X==3&&session.UndoCount==1&&session.Document.Selection is null,"Paste handler failed.");
            session.Undo();Check(ReferenceEquals(doc,session.Document),"Paste Undo failed.");
            ++sequence;var external=pixelClipboard.Read()!.Value;
            Check(!external.PreserveOrigin,"External clipboard change reused old placement.");
            var bytes=external.Content.Pixels.ToRgba();Check(bytes[3]==128&&bytes[7]==100,"PNG clipboard alpha changed.");
            for(int i=0;i<bytes.Length;i++)Check(Math.Abs(bytes[i]-source.Pixels.ToRgba()[i])<=1,"PNG clipboard colors changed.");
            PastePixels(null,new());Check(session.ActiveLayer!.Transform.X==4&&session.ActiveLayer.Transform.Y==4,"External PNG was not centered.");
            session.Load(doc);
            var fallback=new DataObject();fallback.SetData(DataFormats.Bitmap,stored.GetData(DataFormats.Bitmap),true);stored=fallback;++sequence;
            var compatible=pixelClipboard.Read()!.Value;Check(compatible.Content.Pixels.ToRgba().SequenceEqual(source.Pixels.ToRgba()),"Bitmap fallback changed premultiplied colors.");
            CopyPixels(null,new());
            var pngOnly=new DataObject();pngOnly.SetData("PNG",stored.GetData("PNG",false),false);stored=pngOnly;
            Check(!pixelClipboard.Read()!.Value.PreserveOrigin,"Missing ownership marker reused cached origin.");
            CopyPixels(null,new());reject=true;
            bool failed=false;try{SelectionClipboardPixels.Cut(session,pixelClipboard.Write);}catch(IOException){failed=true;}
            Check(failed&&ReferenceEquals(doc,session.Document)&&!session.InTransaction,"Clipboard write failure deleted source.");
            reject=false;CutPixels(null,new());Check(session.ActiveLayer!.Pixels.Tiles.Count==0&&session.UndoCount==1,"Cut handler failed.");
            session.Undo();Check(ReferenceEquals(doc,session.Document),"Cut Undo failed.");
            var invalid=new DataObject();invalid.SetData("PNG",new MemoryStream([1,2,3]),false);stored=invalid;++sequence;
            bool damaged=false;try{pixelClipboard.Read();}catch(InvalidDataException){damaged=true;}
            Check(damaged,"Damaged PNG accepted.");
            stored=new DataObject();++sequence;Check(pixelClipboard.Read() is null,"Empty clipboard pasted stale pixels.");
            File.WriteAllText(path,JsonSerializer.Serialize(new{passed=true,transport="injected IDataObject; system clipboard untouched",checks=new[]{"PNG/bitmap encoding","alpha/color roundtrip","internal origin/external center","clipboard sequence/ownership","copy/cut/paste handlers","failed cut rollback","Undo","corrupt PNG","empty clipboard"}}));
        }
        finally{pixelClipboard=previous;}
    }
}
