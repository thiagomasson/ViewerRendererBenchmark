using System.Diagnostics;
using Microsoft.UI.Xaml.Media;

namespace ViewerRendererBenchmark;

internal static partial class FrameClock
{
    internal static partial void BeginMetrics() { }
    internal static partial FrameStatistics? EndMetrics(double refreshRate) => null;
    internal static partial Task<long> NextAsync()
    {
        var ready = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<object>? callback = null;
        callback = (_, _) => { CompositionTarget.Rendering -= callback; ready.TrySetResult(Stopwatch.GetTimestamp()); };
        CompositionTarget.Rendering += callback;
        return ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}

