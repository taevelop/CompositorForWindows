using Compositor.Core;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Compositor.Imaging;

/// <summary>Local U2Net CPU inference; caller supplies a verified model file.</summary>
public sealed class U2NetPredictor : IDisposable
{
    private readonly InferenceSession session;
    public U2NetPredictor(string modelPath)
    {
        session = new InferenceSession(modelPath);
        var shape = session.InputMetadata.Values.First().Dimensions;
        if (shape.Length != 4 || shape[0] != 1 || shape[1] != 3 || shape[2] != 320 || shape[3] != 320)
        { session.Dispose(); throw new InvalidDataException("Expected a U2Net 1×3×320×320 model input."); }
    }
    public LayerMask Predict(Raster source, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var bitmap = new SKBitmap(CanvasRenderer.Info(source.Width,source.Height));
        source.ToRgba().CopyTo(bitmap.GetPixelSpan());
        using var scaled = new SKBitmap(CanvasRenderer.Info(320,320));
        using(var canvas = new SKCanvas(scaled))
        { canvas.Clear(SKColors.Transparent);canvas.DrawBitmap(bitmap,new SKRect(0,0,320,320),new SKSamplingOptions(SKCubicResampler.CatmullRom)); }
        var input = new DenseTensor<float>(new[] {1,3,320,320});
        float[] mean=[.485f,.456f,.406f], std=[.229f,.224f,.225f];
        float maximum=0;
        for(int y=0;y<320;y++)for(int x=0;x<320;x++)
        {
            var color=scaled.GetPixel(x,y);
            maximum=Math.Max(maximum,Math.Max(color.Red,Math.Max(color.Green,color.Blue)));
            input[0,0,y,x]=color.Red;input[0,1,y,x]=color.Green;input[0,2,y,x]=color.Blue;
        }
        maximum=Math.Max(maximum,1e-6f);
        for(int c=0;c<3;c++)for(int y=0;y<320;y++)for(int x=0;x<320;x++)input[0,c,y,x]=(input[0,c,y,x]/maximum-mean[c])/std[c];
        cancellation.ThrowIfCancellationRequested();
        using var options=new RunOptions();
        using var registration=cancellation.Register(()=>options.Terminate=true);
        using var outputs=session.Run(new[]{NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(),input)},session.OutputMetadata.Keys.Take(1).ToArray(),options);
        cancellation.ThrowIfCancellationRequested();
        var prediction=outputs.First().AsTensor<float>().ToArray();
        if(prediction.Length!=320*320||prediction.Any(v=>!float.IsFinite(v)))throw new InvalidDataException("Invalid U2Net matte output.");
        float minimum=prediction.Min(),range=prediction.Max()-minimum;
        if(range<1e-6f)throw new InvalidOperationException("No distinct foreground subject was detected.");
        using var matte=new SKBitmap(CanvasRenderer.Info(320,320));var bytes=matte.GetPixelSpan();
        for(int p=0;p<prediction.Length;p++){byte gray=(byte)Math.Clamp((prediction[p]-minimum)/range*255,0,255);bytes[p*4]=bytes[p*4+1]=bytes[p*4+2]=gray;bytes[p*4+3]=255;}
        using var full=new SKBitmap(CanvasRenderer.Info(source.Width,source.Height));
        using(var canvas=new SKCanvas(full))canvas.DrawBitmap(matte,new SKRect(0,0,source.Width,source.Height),new SKSamplingOptions(SKCubicResampler.CatmullRom));
        var raster=Raster.FromRgba(source.Width,source.Height,full.GetPixelSpan());var tiles=raster.Tiles.ToBuilder();
        foreach(var(key,tile) in raster.Tiles)
        {
            cancellation.ThrowIfCancellationRequested();var data=tile.Bytes.ToArray();
            for(int i=3;i<data.Length;i+=4)data[i]=255;
            tiles[key]=new(data);
        }
        cancellation.ThrowIfCancellationRequested();return new(new Raster(source.Width,source.Height,tiles.ToImmutable()));
    }
    public void Dispose()=>session.Dispose();
}
