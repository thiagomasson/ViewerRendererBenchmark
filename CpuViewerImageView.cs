using ViewerRendererBenchmark.Infrastructure;
using ViewerRendererBenchmark.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace ViewerRendererBenchmark;

// Retained CPU control for comparison with the production GPU viewer. Both use
// the same decode service, persistent image, and sampling policy.
internal sealed class CpuViewerImageView : SKCanvasView
{
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source), typeof(ViewerImageSource), typeof(CpuViewerImageView), null,
        propertyChanged: (view, _, value) => ((CpuViewerImageView)view).LoadSource((ViewerImageSource?)value));
    private readonly Lock imageSync = new();
    private ViewerDecodedImage? decoded;
    private bool isLoading;
    private int loadVersion;
    private double zoom = 1, offsetX, offsetY;

    public ViewerImageSource? Source { get => (ViewerImageSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public bool IsLoading
    {
        get => isLoading;
        private set { if (isLoading == value) return; isLoading = value; OnPropertyChanged(); }
    }

    public CpuViewerImageView()
    {
        InputTransparent = true;
        IgnorePixelScaling = false;
        Loaded += (_, _) => InvalidateSurface();
    }

    internal void SetTransform(double scale, double x, double y)
    {
        zoom = scale;
        offsetX = x;
        offsetY = y;
        InvalidateSurface();
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

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs args)
    {
        var canvas = args.Surface.Canvas;
        canvas.Clear(SKColors.Black);
        lock (imageSync)
        {
            if (decoded is null || Width <= 0 || Height <= 0) return;
            var pixels = args.Info;
            var size = decoded.Size;
            var fit = Math.Min(pixels.Width / (double)size.Width, pixels.Height / (double)size.Height);
            var width = size.Width * fit * zoom;
            var height = size.Height * fit * zoom;
            var x = (pixels.Width - width) / 2 + offsetX * pixels.Width / Width;
            var y = (pixels.Height - height) / 2 + offsetY * pixels.Height / Height;
            decoded.Draw(canvas, new SKRect((float)x, (float)y, (float)(x + width), (float)(y + height)));
        }
    }
}


