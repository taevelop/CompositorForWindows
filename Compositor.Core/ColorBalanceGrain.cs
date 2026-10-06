namespace Compositor.Core;
public sealed record ColorBalanceAdjustment(double ShadowCyanRed=0, double ShadowMagentaGreen=0, double ShadowYellowBlue=0, double MidCyanRed=0, double MidMagentaGreen=0, double MidYellowBlue=0, double HighlightCyanRed=0, double HighlightMagentaGreen=0, double HighlightYellowBlue=0, bool PreserveLuminosity=true)
{
    public double[] Values => [ShadowCyanRed,ShadowMagentaGreen,ShadowYellowBlue,MidCyanRed,MidMagentaGreen,MidYellowBlue,HighlightCyanRed,HighlightMagentaGreen,HighlightYellowBlue];
    public bool IsIdentity => Values.All(v=>v==0);
    public float[] Shadows => [(float)(ShadowCyanRed/100),(float)(ShadowMagentaGreen/100),(float)(ShadowYellowBlue/100)];
    public float[] Midtones => [(float)(MidCyanRed/100),(float)(MidMagentaGreen/100),(float)(MidYellowBlue/100)];
    public float[] Highlights => [(float)(HighlightCyanRed/100),(float)(HighlightMagentaGreen/100),(float)(HighlightYellowBlue/100)];
    public void Validate() {if(Values.Any(v=>!double.IsFinite(v)||v < -100||v > 100))throw new InvalidDataException("Color balance must be -100..100.");}
}
public sealed record GrainAdjustment(double Amount=25,double Size=1.5,double Roughness=50,uint Seed=0)
{
    public bool IsIdentity => Amount==0;
    public void Validate() {if(!double.IsFinite(Amount)||Amount is < 0 or > 100||!double.IsFinite(Size)||Size is < .5 or > 20||
        !double.IsFinite(Roughness)||Roughness is < 0 or > 100)throw new InvalidDataException("Invalid grain settings.");}
}
