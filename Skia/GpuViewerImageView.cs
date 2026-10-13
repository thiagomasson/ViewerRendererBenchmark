using ViewerRendererBenchmark.Infrastructure;
using ViewerRendererBenchmark.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace ViewerRendererBenchmark.Views;

internal sealed class GpuViewerImageView : SKGLView
{
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source), typeof(ViewerImageSource), typeof(GpuViewerImageView), null,
        propertyChanged: (view, _, value) => ((GpuViewerImageView)view).LoadSource((ViewerImageSource?)value));
    private readonly Lock imageSync = new();
    private ViewerDecodedImage? decoded;
    private bool isLoading;
    private int loadVersion;
    private double zoom = 1, offsetX, offsetY, viewportWidth, viewportHeight;

    public ViewerImageSource? Source { get => (ViewerImageSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public bool IsLoading
    {
        get => isLoading;
        private set { if (isLoading == value) return; isLoading = value; OnPropertyChanged(); }
    }

    public GpuViewerImageView()
    {
        InputTransparent = true;
        IgnorePixelScaling = false;
        HasRenderLoop = false;
        Loaded += (_, _) => InvalidateSurface();
    }

    internal void SetTransform(double scale, double x, double y)
    {
        lock (imageSync)
        {
            zoom = scale;
            offsetX = x;
            offsetY = y;
        }
        InvalidateSurface();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        lock (imageSync) { viewportWidth = width; viewportHeight = height; }
        InvalidateSurface();
    }

    protected override void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        // The GPU handler reports its canvas size from the rendering thread.
        // Viewer layout/decode listeners must still run on the UI thread.
        if (propertyName == nameof(CanvasSize) && !MainThread.IsMainThread)
            MainThread.BeginInvokeOnMainThread(() => base.OnPropertyChanged(propertyName));
        else base.OnPropertyChanged(propertyName);
    }

    internal ViewerPixelSize? PixelTarget(double scale)
    {
        var pixels = CanvasSize;
        return pixels.Width > 0 && pixels.Height > 0
            ? new((int)Math.Clamp(Math.Ceiling(pixels.Width * scale), 1, 32768),
                (int)Math.Clamp(Math.Ceiling(pixels.Height * scale), 1, 32768)) : null;
    }

    private async void LoadSource(ViewerImageSource? source)
    {
        var version = ++loadVersion;
        if (source is null)
        {
            ClearDecoded();
            IsLoading = false;
            InvalidateSurface();
            return;
        }
        IsLoading = true;
        ViewerDecodedImage? replacement = null;
        try
        {
            var service = IPlatformApplication.Current!.Services.GetRequiredService<SkiaViewerImageSourceService>();
            replacement = await service.DecodeAsync(source);
            if (version != loadVersion || source.Token.IsCancellationRequested) return;
            ClearDecoded();
            lock (imageSync) { decoded = replacement; replacement = null; }
            source.Complete();
            InvalidateSurface();
        }
        catch (OperationCanceledException) when (source.Token.IsCancellationRequested) { }
        catch (Exception exception) { source.Fail(exception); }
        finally
        {
            replacement?.Dispose();
            if (version == loadVersion) IsLoading = false;
        }
    }

    private void ClearDecoded()
    {
        lock (imageSync)
        {
            if (decoded is null) return;
            decoded.Dispose();
            decoded = null;
        }
    }

    protected override void OnPaintSurface(SKPaintGLSurfaceEventArgs args)
    {
        var canvas = args.Surface.Canvas;
        canvas.Clear(SKColors.Black);
        lock (imageSync)
        {
            // GPU callbacks can run off the UI thread. Read the image, transform,
            // and DIP viewport from one snapshot without accessing MAUI properties.
            if (decoded is not null && viewportWidth > 0 && viewportHeight > 0)
            {
                var pixels = args.Info;
                var size = decoded.Size;
                var fit = Math.Min(pixels.Width / (double)size.Width, pixels.Height / (double)size.Height);
                var width = size.Width * fit * zoom;
                var height = size.Height * fit * zoom;
                var x = (pixels.Width - width) / 2 + offsetX * pixels.Width / viewportWidth;
                var y = (pixels.Height - height) / 2 + offsetY * pixels.Height / viewportHeight;
                decoded.Draw(canvas, new SKRect((float)x, (float)y, (float)(x + width), (float)(y + height)));
            }
        }
        base.OnPaintSurface(args);
    }
}

