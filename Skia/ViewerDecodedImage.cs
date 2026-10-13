using SkiaSharp;

namespace ViewerRendererBenchmark.Infrastructure;

internal sealed class ViewerDecodedImage(SKBitmap bitmap, ViewerPixelSize originalSize) : IDisposable
{
    private static readonly SKSamplingOptions Smooth = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private static readonly SKSamplingOptions Sharp = new(SKFilterMode.Nearest, SKMipmapMode.None);
    // Keep a stable raster image so Skia can reuse its uploaded texture across
    // GPU redraws. The surface owns its context and texture cache, including
    // context recreation; this object holds no context-bound GPU resources.
    private readonly SKImage image = SKImage.FromBitmap(bitmap);
    internal ViewerPixelSize Size { get; } = new(bitmap.Width, bitmap.Height);

    internal void Draw(SKCanvas canvas, SKRect destination)
    {
        // Select sampling from actual source-pixel magnification. Small images
        // can already be enlarged at fit, so resetting zoom must keep them sharp.
        // Use original dimensions so a pending decode upgrade cannot make the
        // lower-resolution display bitmap look like low-resolution pixel art.
        var pixelScale = Math.Max(destination.Width / originalSize.Width, destination.Height / originalSize.Height);
        var sampling = pixelScale >= 2 ? Sharp : Smooth;
        canvas.DrawImage(image, destination, sampling);
    }
    public void Dispose() { image.Dispose(); bitmap.Dispose(); }
}

