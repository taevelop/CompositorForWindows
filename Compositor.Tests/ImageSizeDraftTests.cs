using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class ImageSizeDraftTests
{
    [Fact] public void PhysicalResolutionChangeKeepsPrintSizeWhenResampling()
    {
        var draft=new ImageSizeDraft(Document.Create(200,100) with{Resolution=100});
        draft.SetUnit(CanvasUnit.Inches);draft.SetResolution(200);
        Assert.Equal(2,draft.Displayed(true));Assert.Equal(new ImageSizeOptions(400,200,200),draft.Options());
        draft.SetUnit(CanvasUnit.Pixels);draft.SetResolution(300);Assert.Equal(400,draft.Options().Width);
    }
    [Fact] public void DisablingResampleRestoresPixelsAndPhysicalSizeChangesOnlyResolution()
    {
        var draft=new ImageSizeDraft(Document.Create(200,100) with{Resolution=100});
        draft.SetDimension(400,true);draft.SetResample(false);
        Assert.Equal(new ImageSizeOptions(200,100,100),draft.Options());Assert.Equal(CanvasUnit.Inches,draft.Unit);
        draft.SetDimension(1,true);Assert.Equal(new ImageSizeOptions(200,100,200),draft.Options());
        draft.SetUnit(CanvasUnit.Centimeters);draft.SetDimension(2.54,false);Assert.Equal(100,draft.Resolution);
        Assert.Throws<ArgumentOutOfRangeException>(()=>draft.SetUnit(CanvasUnit.Pixels));
    }
    [Fact] public void LockUsesCurrentAspectRatherThanOriginalAfterUnlockedEdit()
    {
        var draft=new ImageSizeDraft(Document.Create(200,100)){Locked=false};
        draft.SetDimension(200,false);draft.Locked=true;draft.SetDimension(400,true);
        Assert.Equal(new ImageSizeOptions(400,400,72),draft.Options());
        draft.SetUnit(CanvasUnit.Percent);Assert.Equal(200,draft.Displayed(true));Assert.Equal(400,draft.Displayed(false));
        draft.SetResolution(10000);Assert.Throws<InvalidDataException>(()=>draft.Options());
        Assert.Throws<ArgumentOutOfRangeException>(()=>draft.SetDimension(double.NaN,true));
    }
}
