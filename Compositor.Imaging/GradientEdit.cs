using Compositor.Core;
namespace Compositor.Imaging;

/// <summary>UI-thread owned gradient transaction; only immutable computation runs on workers.</summary>
public sealed class GradientEdit : IDisposable
{
    private readonly EditorSession session;
    private readonly Document original;
    private readonly Guid layerId;
    private readonly bool mask;
    private readonly SemaphoreSlim computation=new(1,1);
    private Document preview;
    private CancellationTokenSource? cancellation;
    private long generation;
    private bool finished,committing;
    public Task Pending {get;private set;}=Task.CompletedTask;
    public GradientEdit(EditorSession session)
    {
        if(session.InTransaction||session.ActiveLayer is not{} layer||layer.IsGroup||(!session.EditMask&&layer.IsAdjustment)||
            (session.EditMask&&layer.Mask is not{Enabled:true}))throw new InvalidOperationException("Select paintable image pixels or an enabled mask.");
        this.session=session;original=preview=session.Document;layerId=layer.Id;mask=session.EditMask;session.Begin();
    }
    private bool OwnsPreview=>!finished&&session.InTransaction&&ReferenceEquals(session.Document,preview)&&session.ActiveLayerId==layerId&&session.EditMask==mask;
    public Task UpdateAsync(GradientFillSettings settings)
    {
        ObjectDisposedException.ThrowIf(finished,this);if(committing)throw new InvalidOperationException("Gradient is being committed.");settings.Validate();
        if(!OwnsPreview)throw new InvalidOperationException("Gradient target changed.");
        cancellation?.Cancel();var requestCancellation=new CancellationTokenSource();cancellation=requestCancellation;
        return Pending=UpdateCore(settings,++generation,requestCancellation);
    }
    private async Task UpdateCore(GradientFillSettings settings,long request,CancellationTokenSource source)
    {
        bool entered=false;
        try
        {
            await computation.WaitAsync(source.Token);entered=true;
            var result=await Task.Run(()=>GradientFill.Apply(original,layerId,settings,mask,source.Token));
            source.Token.ThrowIfCancellationRequested();
            if(request!=generation||!OwnsPreview)return;
            preview=result;session.Preview(result);
        }
        catch(OperationCanceledException) when(source.IsCancellationRequested){}
        finally{if(entered)computation.Release();if(ReferenceEquals(cancellation,source))cancellation=null;source.Dispose();}
    }
    public async Task CommitAsync()
    {
        ObjectDisposedException.ThrowIf(finished,this);if(committing)throw new InvalidOperationException("Gradient is already being committed.");
        committing=true;
        try
        {
            await Pending;
            if(!OwnsPreview)throw new InvalidOperationException("Gradient target changed before commit.");
            session.Commit();finished=true;
        }
        finally{committing=false;}
    }
    public void Dispose()
    {
        if(finished)return;bool restore=OwnsPreview;finished=true;generation++;cancellation?.Cancel();
        if(restore)session.Cancel();
    }
}
