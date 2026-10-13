namespace ViewerRendererBenchmark.Infrastructure;

// A native display bitmap from the original or existing cloud preview. This never
// creates a resized file. Its lifetime follows one of the viewer's three slides.
public sealed partial class ViewerImageSource : ImageSource
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object nativeImageLock = new();
    private bool nativeImageRetained;
    private Action? deferredNativeDisposal;

    internal ViewerImageSource(string? filePath, byte[]? bytes, ViewerPixelSize target, bool zoomed)
    {
        FilePath = filePath;
        Bytes = bytes;
        Target = target;
        MaximumPixels = zoomed ? ViewerImageSizing.ZoomPixelLimit : ViewerImageSizing.ScreenPixelLimit;
    }

    internal string? FilePath { get; }
    internal byte[]? Bytes { get; }
    internal ViewerPixelSize Target { get; }
    internal long MaximumPixels { get; }
    internal CancellationToken Token => lifetime.Token;
    internal Task Ready => ready.Task;
    internal bool Failed { get; private set; }
    internal bool Cancelled => lifetime.IsCancellationRequested;
    internal ViewerPixelSize? OriginalSize { get; private set; }
    internal ViewerPixelSize? DecodedSize { get; private set; }
    internal int PixelReplication { get; private set; } = 1;
    internal bool PreserveNativeRendering { get; private set; }
    public override bool IsEmpty => FilePath is null && Bytes is null;

    internal Stream OpenRead() => FilePath is { } path
        ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)
        : new MemoryStream(Bytes!, writable: false);

    internal void Complete() => ready.TrySetResult();

    // MAUI disposes the previous result when the next load starts. Keep its native
    // bitmap alive until the slide observes that the replacement was applied.
    internal void RetainNativeImage()
    {
        lock (nativeImageLock) nativeImageRetained = true;
    }

    internal void ReleaseNativeImage()
    {
        Action? dispose;
        lock (nativeImageLock)
        {
            nativeImageRetained = false;
            dispose = deferredNativeDisposal;
            deferredNativeDisposal = null;
        }
        dispose?.Invoke();
    }

    internal void DisposeNativeImage(Action dispose)
    {
        lock (nativeImageLock)
        {
            if (nativeImageRetained)
            {
                deferredNativeDisposal += dispose;
                return;
            }
        }
        dispose();
    }

    internal void SetSizes(ViewerPixelSize original, ViewerImageDecodePlan plan, bool preserveNativeRendering)
    {
        OriginalSize = original;
        DecodedSize = plan.Size;
        PixelReplication = plan.PixelReplication;
        PreserveNativeRendering = preserveNativeRendering;
    }
    internal void Fail(Exception exception)
    {
        Failed = true;
        ready.TrySetException(exception);
        _ = ready.Task.Exception; // Neighbors need not await a failed speculative decode.
    }

    public override Task<bool> Cancel()
    {
        lifetime.Cancel();
        ready.TrySetCanceled();
        return Task.FromResult(true);
    }
}

