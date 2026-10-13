using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Widget;
using ViewerRendererBenchmark.Infrastructure;

namespace ViewerRendererBenchmark.Services;

public sealed partial class ViewerImageSourceService
{
    public override async Task<IImageSourceServiceResult?> LoadDrawableAsync(IImageSource imageSource,
        ImageView imageView, CancellationToken cancellationToken = default)
    {
        var source = (ViewerImageSource)imageSource;
        var result = await GetDrawableAsync(source, imageView.Context!, cancellationToken);
        if (result is null) return null;
        try
        {
            source.Token.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            imageView.SetImageDrawable(result.Value);
            source.Complete();
            return new ImageSourceServiceLoadResult(dispose: result.Dispose);
        }
        catch { result.Dispose(); throw; }
    }

    public override async Task<IImageSourceServiceResult<Drawable>?> GetDrawableAsync(IImageSource imageSource,
        Context context, CancellationToken cancellationToken = default)
    {
        var source = (ViewerImageSource)imageSource;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(source.Token, cancellationToken);
        var token = linked.Token;
        var acquired = false;
        try
        {
            await decodeGate.WaitAsync(token);
            acquired = true;
            var drawable = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using var file = source.FilePath is { } path ? new Java.IO.File(path) : null;
                using var buffer = source.Bytes is { } bytes ? Java.Nio.ByteBuffer.Wrap(bytes) : null;
                using var input = file is not null
                    ? ImageDecoder.CreateSource(file)
                    : ImageDecoder.CreateSource(buffer!);
                using var listener = new HeaderListener(source, token);
                Drawable? decoded = null;
                try
                {
                    decoded = ImageDecoder.DecodeDrawable(input!, listener)!;
                    if (source.PixelReplication > 1 && decoded is BitmapDrawable bitmap)
                    {
                        decoded = null; // ReplicatePixels takes ownership of the temporary original.
                        decoded = ReplicatePixels(bitmap, source.DecodedSize!.Value, context, token);
                    }
                    token.ThrowIfCancellationRequested();
                    var result = decoded!;
                    decoded = null;
                    return result;
                }
                finally { decoded?.Dispose(); }
            }, token);
            return new ImageSourceServiceResult(drawable, dispose: () => source.DisposeNativeImage(() =>
            {
                if (drawable is Android.Graphics.Drawables.IAnimatable animation) animation.Stop();
                drawable.Dispose();
            }));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { _ = source.Cancel(); throw; }
        catch (Exception exception) { ReportFailure(source, exception); throw; }
        finally { if (acquired) decodeGate.Release(); }
    }

    private static BitmapDrawable ReplicatePixels(BitmapDrawable original, ViewerPixelSize size,
        Context context, CancellationToken token)
    {
        using (original)
        using (var bitmap = original.Bitmap!)
        {
            token.ThrowIfCancellationRequested();
            using var expanded = Bitmap.CreateScaledBitmap(bitmap, size.Width, size.Height, filter: false)!;
            try
            {
                token.ThrowIfCancellationRequested();
                return new BitmapDrawable(context.Resources, expanded);
            }
            catch { expanded.Recycle(); throw; }
            finally { bitmap.Recycle(); }
        }
    }

    private sealed class HeaderListener(ViewerImageSource source, CancellationToken token)
        : Java.Lang.Object, ImageDecoder.IOnHeaderDecodedListener
    {
        public void OnHeaderDecoded(ImageDecoder decoder, ImageDecoder.ImageInfo info, ImageDecoder.Source input)
        {
            token.ThrowIfCancellationRequested();
            var original = new ViewerPixelSize(info.Size!.Width, info.Size.Height);
            var preserveNativeRendering = info.IsAnimated || info.MimeType == "image/gif";
            var plan = ViewerImageSizing.DecodePlan(original.Width, original.Height, source.Target,
                source.MaximumPixels, preserveNativeRendering);
            source.SetSizes(original, plan, preserveNativeRendering);
            if (plan.PixelReplication > 1)
            {
                // A software bitmap can be enlarged by the native nearest-neighbor
                // scaler. Decode the original pixels first to avoid filtered upscaling.
                decoder.Allocator = ImageDecoderAllocator.Software;
                decoder.SetTargetSize(original.Width, original.Height);
            }
            else
                decoder.SetTargetSize(plan.Size.Width, plan.Size.Height);
        }
    }
}

