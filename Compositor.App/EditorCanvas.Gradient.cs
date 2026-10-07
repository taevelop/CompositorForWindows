using System.Windows.Input;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
namespace Compositor.App;
public sealed partial class EditorCanvas
{
    private GradientEdit? gradientEdit;
    private Document? gradientRenderDocument;
    private PointD gradientStart,gradientEnd;
    private bool gradientDragging,gradientMovingStart,gradientCommitting;
    public bool HasGradient=>gradientEdit is not null;
    public Func<PointD,PointD,GradientFillSettings> ReadGradient {get;set;}=(start,end)=>new(start,end,0,0,0,255,255,255);
    internal Task GradientPending=>gradientEdit?.Pending??Task.CompletedTask;
    internal bool IsGradientPreparing=>gradientCommitting||gradientEdit is {} edit&&!edit.Pending.IsCompleted;
    private void BeginGradient(PointD point)
    {
        if(gradientCommitting)return;
        bool nearStart=HasGradient&&double.Hypot(point.X-gradientStart.X,point.Y-gradientStart.Y)*Zoom<=8;
        bool nearEnd=HasGradient&&double.Hypot(point.X-gradientEnd.X,point.Y-gradientEnd.Y)*Zoom<=8;
        gradientEdit??=new GradientEdit(Session);
        gradientMovingStart=nearStart;
        if(!nearStart&&!nearEnd)gradientStart=gradientEnd=point;
        gradientDragging=true;gestureButton=MouseButton.Left;UpdateGradient();
    }
    private void MoveGradient(PointD point)
    {
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))point=SnapGradientPoint(point,gradientMovingStart?gradientEnd:gradientStart);
        if(gradientMovingStart)gradientStart=point;else gradientEnd=point;
        UpdateGradient();
    }
    internal static PointD SnapGradientPoint(PointD point,PointD anchor)
    {
        double x=point.X-anchor.X,y=point.Y-anchor.Y,length=double.Hypot(x,y);
        double angle=Math.Round(Math.Atan2(y,x)/(Math.PI/4),MidpointRounding.AwayFromZero)*(Math.PI/4);
        return new(anchor.X+Math.Cos(angle)*length,anchor.Y+Math.Sin(angle)*length);
    }
    public async void UpdateGradient()
    {
        if(gradientEdit is not{} edit||gradientCommitting)return;
        try{var pending=edit.UpdateAsync(ReadGradient(gradientStart,gradientEnd));InvalidateVisual();await pending;if(ReferenceEquals(gradientEdit,edit))gradientRenderDocument=Session.Document;InvalidateVisual();}
        catch(Exception error){if(ReferenceEquals(gradientEdit,edit)){CancelGradient();ReportError?.Invoke(error.Message);}}
    }
    private Task<bool>? gradientCommitTask;
    public Task<bool> CommitGradientAsync()
    {
        if(gradientCommitting)return gradientCommitTask!;
        if(gradientEdit is not{} edit)return Task.FromResult(true);
        gradientCommitting=true;
        InvalidateVisual();
        return gradientCommitTask=CommitGradientCoreAsync(edit);
    }
    private async Task<bool> CommitGradientCoreAsync(GradientEdit edit)
    {
        try{await edit.CommitAsync();gradientRenderDocument=Session.Document;return true;}
        catch(Exception error){edit.Dispose();ReportError?.Invoke(error.Message);return false;}
        finally{if(ReferenceEquals(gradientEdit,edit))gradientEdit=null;gradientCommitting=false;gradientDragging=false;InvalidateVisual();}
    }
    public void CancelGradient()
    {var edit=gradientEdit;gradientEdit=null;gradientDragging=false;edit?.Dispose();InvalidateVisual();}
    private void DrawGradient(SKCanvas canvas)
    {
        if(!HasGradient)return;
        using var outline=new SKPaint{Color=SKColors.Black,StrokeWidth=(float)(3/Zoom),Style=SKPaintStyle.Stroke,IsAntialias=true};
        using var line=new SKPaint{Color=new SKColor(108,154,224),StrokeWidth=(float)(1/Zoom),Style=SKPaintStyle.Stroke,IsAntialias=true};
        var a=new SKPoint((float)gradientStart.X,(float)gradientStart.Y);var b=new SKPoint((float)gradientEnd.X,(float)gradientEnd.Y);
        canvas.DrawLine(a,b,outline);canvas.DrawLine(a,b,line);
        canvas.DrawCircle(a,(float)(5/Zoom),outline);canvas.DrawCircle(a,(float)(5/Zoom),line);
        canvas.DrawCircle(b,(float)(5/Zoom),outline);canvas.DrawCircle(b,(float)(5/Zoom),line);
    }
    internal void DrawGradientStatus(SKCanvas canvas)
    {
        if(!IsGradientPreparing)return;
        using var background=new SKPaint{Color=new SKColor(28,30,34,230)};
        using var ink=new SKPaint{Color=new SKColor(220,230,245),IsAntialias=true};
        using var font=new SKFont{Size=13};
        canvas.DrawRoundRect(new SKRect(12,12,290,44),4,4,background);
        canvas.DrawText(gradientCommitting?"Applying gradient…":"Updating gradient… · Esc to cancel",22,33,SKTextAlign.Left,font,ink);
    }
}
