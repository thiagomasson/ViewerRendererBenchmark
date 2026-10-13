using Microsoft.Extensions.Logging;
using ViewerRendererBenchmark.Infrastructure;

namespace ViewerRendererBenchmark.Services;

public sealed partial class SkiaViewerImageSourceService(ILogger<SkiaViewerImageSourceService> logger)
{
    // Bound temporary decode allocations as well as the three retained slide images.
    private readonly SemaphoreSlim decodeGate = new(2, 2);
    internal SemaphoreSlim DecodeGate => decodeGate;

    internal async Task<ViewerDecodedImage> DecodeAsync(ViewerImageSource source)
    {
        var token = source.Token;
        await decodeGate.WaitAsync(token);
        try
        {
            var image = await Task.Run(() => DecodeCoreAsync(source, token), token);
            if (token.IsCancellationRequested) { image.Dispose(); token.ThrowIfCancellationRequested(); }
            return image;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { _ = source.Cancel(); throw; }
        catch (Exception exception) { ReportFailure(source, exception); throw; }
        finally { decodeGate.Release(); }
    }

    private partial Task<ViewerDecodedImage> DecodeCoreAsync(ViewerImageSource source, CancellationToken token);

    private void ReportFailure(ViewerImageSource source, Exception exception)
    {
        source.Fail(exception);
        if (logger.IsEnabled(LogLevel.Warning))
            logger.LogWarning(exception, "Viewer image could not be decoded (HRESULT {HResult}).", $"0x{exception.HResult:X8}");
    }
}

