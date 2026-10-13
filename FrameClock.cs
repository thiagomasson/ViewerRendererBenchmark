using System.Diagnostics;

namespace ViewerRendererBenchmark;

internal static partial class FrameClock
{
    internal static partial Task<long> NextAsync();
    internal static partial void BeginMetrics();
    internal static partial FrameStatistics? EndMetrics(double refreshRate);
    internal static double CpuMilliseconds()
    {
#if ANDROID
        return Android.OS.Process.ElapsedCpuTime;
#else
        using var process = Process.GetCurrentProcess();
        return process.TotalProcessorTime.TotalMilliseconds;
#endif
    }
}

internal sealed record FrameStatistics(int Samples, double MedianMs, double P95Ms, int MissedDeadlines, int DroppedMetricCallbacks);

