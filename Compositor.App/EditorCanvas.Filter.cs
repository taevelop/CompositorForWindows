using Compositor.Core;
namespace Compositor.App;
public sealed partial class EditorCanvas
{
    private Document? filterDisplay,filterOriginal;
    private long filterGeneration;
    internal void ShowFilterPreview(Document? document)
    {
        filterDisplay=document;filterOriginal=document is null?null:Session.Document;
        filterGeneration=document is null?0:Session.TransactionGeneration;InvalidateVisual();
    }
}
