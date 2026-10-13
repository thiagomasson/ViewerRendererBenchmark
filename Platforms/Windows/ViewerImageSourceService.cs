using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using ViewerRendererBenchmark.Infrastructure;
using Windows.Graphics.Imaging;
using WImageSource = Microsoft.UI.Xaml.Media.ImageSource;

namespace ViewerRendererBenchmark.Services;

public sealed partial class ViewerImageSourceService
{
    public override async Task<IImageSourceServiceResult<WImageSource>?> GetImageSourceAsync(IImageSource imageSource,
        float scale = 1, CancellationToken cancellationToken = default)
    {
        var source = (ViewerImageSource)imageSource;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(source.Token, cancellationToken);
        var token = linked.Token;
        var acquired = false;
        try
        {
            await decodeGate.WaitAsync(token);
            acquired = true;
            // Header probing and file opening are off the UI thread. BitmapImage is a
            // UI object; its asynchronous native decoder also keeps GIF playback intact.
            using var stream = await Task.Run(source.OpenRead, token);
            using var input = stream.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(input).AsTask(token);
            var original = new ViewerPixelSize((int)decoder.OrientedPixelWidth, (int)decoder.OrientedPixelHeight);
            var preserveNativeRendering = decoder.FrameCount > 1
                || decoder.DecoderInformation.CodecId == BitmapDecoder.GifDecoderId;
            var plan = ViewerImageSizing.DecodePlan(original.Width, original.Height, source.Target,
                source.MaximumPixels, preserveNativeRendering);
            source.SetSizes(original, plan, preserveNativeRendering);
            if (plan.PixelReplication > 1)
            {
                var replicated = await DecodeReplicatedAsync(decoder, plan.PixelReplication, token);
                source.Complete();
                return new ImageSourceServiceResult(replicated,
                    dispose: () => source.DisposeNativeImage(
                        () => MainThread.BeginInvokeOnMainThread(replicated.Dispose)));
            }
            var size = plan.Size;
            input.Seek(0);
            var image = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                var bitmap = new BitmapImage
                {
                    DecodePixelType = DecodePixelType.Physical,
                    DecodePixelWidth = size.Width,
                    DecodePixelHeight = size.Height
                };
                try { await bitmap.SetSourceAsync(input).AsTask(token); }
                catch { bitmap.UriSource = null; throw; }
                token.ThrowIfCancellationRequested();
                return bitmap;
            });
            source.Complete();
            return new ImageSourceServiceResult(image);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { _ = source.Cancel(); throw; }
        catch (Exception exception) { ReportFailure(source, exception); throw; }
        finally { if (acquired) decodeGate.Release(); }
    }

    private static async Task<SoftwareBitmapSource> DecodeReplicatedAsync(BitmapDecoder decoder,
        int replication, CancellationToken token)
    {
        // Scaling precedes EXIF rotation, so use the un-oriented dimensions here.
        // Integer nearest-neighbor scaling repeats each original pixel without
        // changing its color. The native Image control still draws the final bitmap.
        using var pixels = await Task.Run(async () =>
        {
            var transform = new BitmapTransform
            {
                ScaledWidth = decoder.PixelWidth * (uint)replication,
                ScaledHeight = decoder.PixelHeight * (uint)replication,
                InterpolationMode = BitmapInterpolationMode.NearestNeighbor
            };
            return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(token);
        }, token);
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var bitmap = new SoftwareBitmapSource();
            try
            {
                await bitmap.SetBitmapAsync(pixels).AsTask(token);
                token.ThrowIfCancellationRequested();
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        });
    }
}

