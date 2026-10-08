namespace Compositor.Imaging;
public enum BlurKind {Gaussian,Motion}
public sealed record BlurSettings(BlurKind Kind,double Amount,double Angle=0)
{
    public int Margin=>(int)Math.Ceiling(Kind==BlurKind.Gaussian?Amount*3+2:Amount/2+2);
    public int MaximumPadding=>Kind==BlurKind.Gaussian?752:1002;
    public void Validate()
    {
        if(!Enum.IsDefined(Kind)||!double.IsFinite(Amount)||Amount<(Kind==BlurKind.Gaussian?.1:1)||Amount>(Kind==BlurKind.Gaussian?250:2000)||
            !double.IsFinite(Angle)||Angle< -90||Angle>90)throw new ArgumentOutOfRangeException(nameof(Amount));
    }
}
