namespace Compositor.Core;
public sealed record BlackWhiteAdjustment(double Reds=40, double Yellows=60, double Greens=40, double Cyans=60,
    double Blues=20, double Magentas=80, bool Tint=false, double TintHue=40, double TintSaturation=20)
{
    public void Validate()
    {
        if (new[]{Reds,Yellows,Greens,Cyans,Blues,Magentas}.Any(v=>!double.IsFinite(v)||v < -200||v > 300) ||
            !double.IsFinite(TintHue)||TintHue is < 0 or > 360 || !double.IsFinite(TintSaturation)||TintSaturation is < 0 or > 100)
            throw new InvalidDataException("Invalid Black & White settings.");
    }
    public float[] Weights => [(float)(Reds/100),(float)(Yellows/100),(float)(Greens/100),(float)(Cyans/100),(float)(Blues/100),(float)(Magentas/100)];
}
