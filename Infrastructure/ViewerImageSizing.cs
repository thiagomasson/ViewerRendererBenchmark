namespace ViewerRendererBenchmark.Infrastructure;

internal readonly record struct ViewerPixelSize(int Width, int Height);
internal readonly record struct ViewerImageDecodePlan(ViewerPixelSize Size, int PixelReplication);

internal static class ViewerImageSizing
{
    public const double MaximumZoom = 128;
    public const long ScreenPixelLimit = 8L * 1024 * 1024;
    public const long ZoomPixelLimit = 24L * 1024 * 1024;
    private const int MaximumReplicatedDimension = 16384;

    public static ViewerImageDecodePlan DecodePlan(int width, int height, ViewerPixelSize target,
        long maximumPixels, bool preserveNativeRendering = false)
    {
        var fitted = Fit(width, height, target.Width, target.Height, maximumPixels);
        // AspectFit uses the smaller ratio, including letterboxing. Target already
        // includes display density and zoom, so this is physical pixels per source pixel.
        var displayScale = Math.Min(target.Width / (double)width, target.Height / (double)height);
        if (!preserveNativeRendering && displayScale >= 2)
        {
            var pixelLimit = Math.Floor(Math.Sqrt(maximumPixels / ((double)width * height)));
            var dimensionLimit = MaximumReplicatedDimension / Math.Max(width, height);
            var replication = (int)Math.Min(Math.Ceiling(displayScale), Math.Min(pixelLimit, dimensionLimit));
            if (replication >= 2)
                return new(new(width * replication, height * replication), replication);
        }
        return new(fitted, 1);
    }

    public static ViewerPixelSize Fit(int width, int height, int targetWidth, int targetHeight, long maximumPixels)
    {
        if (width <= 0 || height <= 0) throw new InvalidDataException("The image has invalid dimensions.");
        var scale = Math.Min(1d, Math.Min(Math.Max(1, targetWidth) / (double)width, Math.Max(1, targetHeight) / (double)height));
        scale = Math.Min(scale, Math.Sqrt(maximumPixels / ((double)width * height)));
        return new(Math.Max(1, (int)Math.Floor(width * scale)), Math.Max(1, (int)Math.Floor(height * scale)));
    }

    public static ViewerPixelSize Viewport(double width, double height, double density, double zoom = 1)
    {
        var factor = Math.Max(0.1, density) * Math.Clamp(zoom, 1, MaximumZoom);
        return new((int)Math.Clamp(Math.Ceiling(Math.Max(1, width) * factor), 1, 32768),
            (int)Math.Clamp(Math.Ceiling(Math.Max(1, height) * factor), 1, 32768));
    }

    public static double FillZoom(double width, double height, int contentWidth, int contentHeight)
    {
        if (width <= 0 || height <= 0 || contentWidth <= 0 || contentHeight <= 0) return 1;
        var widthScale = width / contentWidth;
        var heightScale = height / contentHeight;
        // The image starts at AspectFit; fill the viewport by covering its shorter fitted dimension.
        return Math.Clamp(Math.Max(widthScale, heightScale) / Math.Min(widthScale, heightScale), 1, MaximumZoom);
    }

    public static (double X, double Y) PanLimits(double width, double height, int contentWidth, int contentHeight, double zoom)
    {
        if (width <= 0 || height <= 0) return (0, 0);
        var fittedWidth = width;
        var fittedHeight = height;
        if (contentWidth > 0 && contentHeight > 0)
        {
            var fit = Math.Min(width / contentWidth, height / contentHeight);
            fittedWidth = contentWidth * fit;
            fittedHeight = contentHeight * fit;
        }
        return (Math.Max(0, (fittedWidth * zoom - width) / 2),
            Math.Max(0, (fittedHeight * zoom - height) / 2));
    }
}

