namespace ViewerRendererBenchmark.Infrastructure;

// Adapt bounded Skia decoding to the shared bitmap request metadata while
// keeping pixel replication exclusive to the native duplication renderer.
public sealed partial class ViewerImageSource
{
    internal void SetSizes(ViewerPixelSize original, ViewerPixelSize decoded) =>
        SetSizes(original, new ViewerImageDecodePlan(decoded, 1), preserveNativeRendering: false);
}

