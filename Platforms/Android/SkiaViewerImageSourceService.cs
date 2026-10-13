using System.Runtime.InteropServices;
using Android.Graphics;
using ViewerRendererBenchmark.Infrastructure;
using SkiaSharp;
using ABitmap = Android.Graphics.Bitmap;

namespace ViewerRendererBenchmark.Services;

public sealed partial class SkiaViewerImageSourceService
{
    private partial Task<ViewerDecodedImage> DecodeCoreAsync(ViewerImageSource source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var file = source.FilePath is { } path ? new Java.IO.File(path) : null;
        using var buffer = source.Bytes is { } bytes ? Java.Nio.ByteBuffer.Wrap(bytes) : null;
        using var input = file is not null ? ImageDecoder.CreateSource(file) : ImageDecoder.CreateSource(buffer!);
        using var listener = new HeaderListener(source, token);
        using var nativeBitmap = ImageDecoder.DecodeBitmap(input!, listener)!;
        try
        {
            token.ThrowIfCancellationRequested();
            var bitmap = new SKBitmap(new SKImageInfo(nativeBitmap.Width, nativeBitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            try
            {
                using var converted = nativeBitmap.GetConfig()?.Equals(ABitmap.Config.Argb8888) == true
                    ? null : nativeBitmap.Copy(ABitmap.Config.Argb8888!, false)
                        ?? throw new InvalidDataException("The decoded image could not be converted to display pixels.");
                try { CopyPixels(converted ?? nativeBitmap, bitmap); }
                finally { converted?.Recycle(); }
                bitmap.SetImmutable();
                return Task.FromResult(new ViewerDecodedImage(bitmap, source.OriginalSize!.Value));
            }
            catch { bitmap.Dispose(); throw; }
        }
        finally { nativeBitmap.Recycle(); }
    }

    // Copy only the bounded decoded bitmap without allocating a second managed
    // pixel array. GIF drawables stay entirely with the native image service.
    private static unsafe void CopyPixels(ABitmap source, SKBitmap target)
    {
        if (LockPixels(Android.Runtime.JNIEnv.Handle, source.Handle, out var pixels) != 0)
            throw new IOException("The decoded image pixels could not be read.");
        try
        {
            var destination = (byte*)target.GetPixels();
            var rowBytes = checked(source.Width * 4);
            for (var row = 0; row < source.Height; row++)
                Buffer.MemoryCopy((byte*)pixels + row * source.RowBytes,
                    destination + row * target.RowBytes, target.RowBytes, rowBytes);
            target.NotifyPixelsChanged();
        }
        finally { _ = UnlockPixels(Android.Runtime.JNIEnv.Handle, source.Handle); }
    }

    [DllImport("libjnigraphics.so", EntryPoint = "AndroidBitmap_lockPixels")]
    private static extern int LockPixels(IntPtr environment, IntPtr bitmap, out IntPtr pixels);
    [DllImport("libjnigraphics.so", EntryPoint = "AndroidBitmap_unlockPixels")]
    private static extern int UnlockPixels(IntPtr environment, IntPtr bitmap);

    private sealed class HeaderListener(ViewerImageSource source, CancellationToken token)
        : Java.Lang.Object, ImageDecoder.IOnHeaderDecodedListener
    {
        public void OnHeaderDecoded(ImageDecoder decoder, ImageDecoder.ImageInfo info, ImageDecoder.Source input)
        {
            token.ThrowIfCancellationRequested();
            var size = ViewerImageSizing.Fit(info.Size!.Width, info.Size.Height,
                source.Target.Width, source.Target.Height, source.MaximumPixels);
            source.SetSizes(new(info.Size.Width, info.Size.Height), size);
            decoder.SetTargetSize(size.Width, size.Height);
            decoder.Allocator = ImageDecoderAllocator.Software;
            using var colorSpace = Android.Graphics.ColorSpace.Get(Android.Graphics.ColorSpace.Named.Srgb);
            decoder.SetTargetColorSpace(colorSpace!);
        }
    }
}

