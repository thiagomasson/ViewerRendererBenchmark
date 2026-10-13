using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using ViewerRendererBenchmark.Infrastructure;
using SkiaSharp;
using Windows.Graphics.Imaging;

namespace ViewerRendererBenchmark.Services;

public sealed partial class SkiaViewerImageSourceService
{
    private partial async Task<ViewerDecodedImage> DecodeCoreAsync(ViewerImageSource source, CancellationToken token)
    {
        using var stream = source.OpenRead();
        using var input = stream.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(input).AsTask(token);
        var original = new ViewerPixelSize(checked((int)decoder.OrientedPixelWidth), checked((int)decoder.OrientedPixelHeight));
        var size = ViewerImageSizing.Fit(original.Width, original.Height, source.Target.Width, source.Target.Height, source.MaximumPixels);
        source.SetSizes(original, size);
        var swapAxes = decoder.OrientedPixelWidth != decoder.PixelWidth;
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)(swapAxes ? size.Height : size.Width),
            ScaledHeight = (uint)(swapAxes ? size.Width : size.Height)
        };
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(token);
        var pixels = data.DetachPixelData();
        token.ThrowIfCancellationRequested();
        var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        try
        {
            if (pixels.Length != checked(size.Width * size.Height * 4))
                throw new InvalidDataException("The image decoder returned unexpected pixel dimensions.");
            Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
            bitmap.SetImmutable();
            return new ViewerDecodedImage(bitmap, original);
        }
        catch { bitmap.Dispose(); throw; }
    }
}

