using Microsoft.Extensions.Logging;
using ViewerRendererBenchmark.Infrastructure;

namespace ViewerRendererBenchmark.Services;

public sealed partial class ViewerImageSourceService(ILogger<ViewerImageSourceService> logger)
    : ImageSourceService(logger), IImageSourceService<ViewerImageSource>
{
    // Bound temporary decode allocations as well as the three retained slide images.
    private readonly SemaphoreSlim decodeGate = new(2, 2);

    private void ReportFailure(ViewerImageSource source, Exception exception)
    {
        source.Fail(exception);
        if (Logger?.IsEnabled(LogLevel.Warning) == true)
            Logger.LogWarning(exception, "Viewer image could not be decoded (HRESULT {HResult}).", $"0x{exception.HResult:X8}");
    }
}

