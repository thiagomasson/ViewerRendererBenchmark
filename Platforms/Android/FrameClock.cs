using System.Diagnostics;
using Android.Views;
using Android.OS;

namespace ViewerRendererBenchmark;

internal static partial class FrameClock
{
    private static readonly Lock metricsSync = new();
    private static readonly List<double> durations = [];
    private static bool collecting;
    private static int dropped;
    private static HandlerThread? metricThread;
    private static MetricsListener? listener;

    internal static void Attach(Android.Views.Window window)
    {
        metricThread = new HandlerThread("Renderer benchmark frame metrics");
        metricThread.Start();
        listener = new MetricsListener();
        window.AddOnFrameMetricsAvailableListener(listener, new Handler(metricThread.Looper!));
    }

    internal static partial void BeginMetrics()
    {
        lock (metricsSync) { durations.Clear(); dropped = 0; collecting = true; }
    }

    internal static partial FrameStatistics? EndMetrics(double refreshRate)
    {
        lock (metricsSync)
        {
            collecting = false;
            if (durations.Count == 0) return null;
            var sorted = durations.Order().ToArray();
            return new(sorted.Length, sorted[(sorted.Length - 1) / 2], sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.95) - 1)],
                sorted.Count(value => value > 1000 / Math.Max(1, refreshRate)), dropped);
        }
    }

    private sealed class MetricsListener : Java.Lang.Object, Android.Views.Window.IOnFrameMetricsAvailableListener
    {
        public void OnFrameMetricsAvailable(Android.Views.Window? window, FrameMetrics? frame, int dropCountSinceLastInvocation)
        {
            lock (metricsSync)
            {
                if (!collecting || frame is null) return;
                durations.Add(frame.GetMetric((int)FrameMetricsId.TotalDuration) / 1_000_000d);
                dropped += dropCountSinceLastInvocation;
            }
        }
    }

    internal static partial Task<long> NextAsync()
    {
        var callback = new Callback();
        Choreographer.Instance!.PostFrameCallback(callback);
        return callback.Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class Callback : Java.Lang.Object, Choreographer.IFrameCallback
    {
        internal readonly TaskCompletionSource<long> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void DoFrame(long time) { Ready.TrySetResult(Stopwatch.GetTimestamp()); Dispose(); }
    }
}

