using Compositor.Core;
namespace Compositor.Imaging;

/// <summary>Image Size fields keep physical dimensions when changing DPI in a physical unit.</summary>
public sealed class ImageSizeDraft
{
    public int OriginalWidth {get;}
    public int OriginalHeight {get;}
    public double Width {get;private set;}
    public double Height {get;private set;}
    public double Resolution {get;private set;}
    public bool Locked {get;set;}=true;
    public bool Resample {get;private set;}=true;
    public CanvasUnit Unit {get;private set;}=CanvasUnit.Pixels;
    public Sampling Sampling {get;set;}=Sampling.High;
    public ImageSizeDraft(Document document)
    {OriginalWidth=document.Width;OriginalHeight=document.Height;Width=document.Width;Height=document.Height;Resolution=document.Resolution;}
    public void SetUnit(CanvasUnit unit)
    {
        if(!Enum.IsDefined(unit)||(!Resample&&unit is CanvasUnit.Pixels or CanvasUnit.Percent))
            throw new ArgumentOutOfRangeException(nameof(unit));
        Unit=unit;
    }
    public double Displayed(bool widthAxis)
    {
        double pixels=widthAxis?Width:Height;
        return Unit switch{CanvasUnit.Percent=>pixels/(widthAxis?OriginalWidth:OriginalHeight)*100,
            CanvasUnit.Inches=>pixels/Resolution,CanvasUnit.Centimeters=>pixels/Resolution*2.54,_=>pixels};
    }
    public void SetDimension(double value,bool widthAxis)
    {
        if(!double.IsFinite(value)||value<=0)throw new ArgumentOutOfRangeException(nameof(value));
        if(!Resample)
        {
            Resolution=(widthAxis?Width:Height)/value*(Unit==CanvasUnit.Centimeters?2.54:1);return;
        }
        double pixels=Unit switch{CanvasUnit.Percent=>value/100*(widthAxis?OriginalWidth:OriginalHeight),
            CanvasUnit.Inches=>value*Resolution,CanvasUnit.Centimeters=>value/2.54*Resolution,_=>value};
        if(widthAxis){if(Locked)Height=pixels*Height/Width;Width=pixels;}
        else{if(Locked)Width=pixels*Width/Height;Height=pixels;}
    }
    public void SetResolution(double value)
    {
        if(!double.IsFinite(value)||value<=0)throw new ArgumentOutOfRangeException(nameof(value));
        if(Resample&&Unit is CanvasUnit.Inches or CanvasUnit.Centimeters)
        {Width*=value/Resolution;Height*=value/Resolution;}
        Resolution=value;
    }
    public void SetResample(bool enabled)
    {
        Resample=enabled;
        if(!enabled)
        {
            Width=OriginalWidth;Height=OriginalHeight;Locked=true;
            if(Unit is CanvasUnit.Pixels or CanvasUnit.Percent)Unit=CanvasUnit.Inches;
        }
    }
    public ImageSizeOptions Options()
    {
        double width=Math.Round(Width,MidpointRounding.AwayFromZero),height=Math.Round(Height,MidpointRounding.AwayFromZero);
        if(!double.IsFinite(width)||!double.IsFinite(height)||width<1||height<1||width>30000||height>30000)
            throw new InvalidDataException("Use 1–30,000 pixels per side.");
        var result=new ImageSizeOptions((int)width,(int)height,Resolution,Sampling);result.Validate();return result;
    }
}
