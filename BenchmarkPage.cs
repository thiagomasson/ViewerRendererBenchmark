using System.Diagnostics;
using System.Text.Json;
using ViewerRendererBenchmark.Infrastructure;
using ViewerRendererBenchmark.Views;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace ViewerRendererBenchmark;

internal sealed class BenchmarkPage : ContentPage
{
    private static readonly string[] Modes = ["native", "native-pixel-duplication", "skia-cpu", "skia-gpu", "webview"];
    private readonly Label status = new() { Text = "Preparing renderer benchmark…", TextColor = Colors.White, FontSize = 16 };
    private readonly Grid viewport = new() { BackgroundColor = Colors.Black, IsClippedToBounds = true };
    private readonly Image native = new() { Aspect = Aspect.AspectFit, IsAnimationPlaying = false };
    private readonly Grid nativeHost = new() { BackgroundColor = Colors.Black };
    private readonly GpuViewerImageView skia = new();
    private readonly CpuViewerImageView cpuSkia = new();
    private readonly WebViewer web = new();
    private readonly BenchmarkReport report = new();
    private bool started;
    private ViewerImageSource? source;
    private long gpuPaints;
    private int gpuContextObserved;
    private readonly Dictionary<string, ViewerPixelSize> originals = [];

    internal BenchmarkPage()
    {
        BackgroundColor = Color.FromArgb("#172438");
        nativeHost.Children.Add(native);
        viewport.Children.Add(nativeHost);
        viewport.Children.Add(skia);
        viewport.Children.Add(cpuSkia);
        viewport.Children.Add(web);
        skia.PaintSurface += (_, _) =>
        {
            if (skia.GRContext is null) return;
            Interlocked.Exchange(ref gpuContextObserved, 1);
            Interlocked.Increment(ref gpuPaints);
        };
        var layout = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)], Padding = 12, RowSpacing = 10 };
        layout.Children.Add(status);
        Grid.SetRow(viewport, 1);
        layout.Children.Add(viewport);
        Content = layout;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (started) return;
        started = true;
        try
        {
            await Task.Delay(1200);
            for (var retry = 0; viewport.Width <= 0 && retry < 20; retry++) await Task.Delay(100);
            report.Platform = DeviceInfo.Current.Platform.ToString();
            report.Device = DeviceInfo.Current.Model;
            report.OperatingSystem = DeviceInfo.Current.VersionString;
            report.ViewportDip = [viewport.Width, viewport.Height];
            report.Density = Density();
            report.RefreshRateHz = DeviceDisplay.Current.MainDisplayInfo.RefreshRate;
            Save();
            var paths = await Task.Run(() => BenchmarkFixtures.Create(Path.Combine(FileSystem.AppDataDirectory, "fixtures")));
            foreach (var path in paths)
            {
                using var codec = SKCodec.Create(path);
                originals[path] = new(codec.Info.Width, codec.Info.Height);
            }
            foreach (var path in paths)
            {
                foreach (var zoom in new[] { 1d, 4d }) await BenchmarkLoadsAsync(path, zoom);
            }
            for (var round = 0; round < 2; round++)
                foreach (var path in paths)
                    foreach (var stage in new[] { "fit-motion", "zoom-pan" })
                        foreach (var mode in round == 0 ? Modes : Modes.Reverse())
                            await BenchmarkFramesAsync(path, stage, mode, round);
            await VerifyGpuSamplingAsync(paths.Single(path => Path.GetFileName(path) == "pixel-256.png"));
            await ClearAsync();
            report.CompletedUtc = DateTimeOffset.UtcNow;
            Save();
            status.Text = "Benchmark complete. Results have been saved.";
        }
        catch (Exception exception) { report.Error = exception.ToString(); Save(); status.Text = "Benchmark failed: " + exception.Message; }
    }

    private async Task BenchmarkLoadsAsync(string path, double zoom)
    {
        var samples = Modes.ToDictionary(mode => mode, _ => new List<double>());
        var cpu = Modes.ToDictionary(mode => mode, _ => new List<double>());
        var sizes = new Dictionary<string, ViewerPixelSize>();
        var replications = new Dictionary<string, int>();
        for (var iteration = -2; iteration < 6; iteration++)
            foreach (var mode in Enumerable.Range(0, Modes.Length).Select(index => Modes[(index + iteration + 2) % Modes.Length]))
            {
                status.Text = $"Loading {Path.GetFileName(path)}, {mode}, target zoom {zoom}, sample {iteration + 1}";
                await ClearAsync();
                await Task.Delay(100);
                var beforeCpu = FrameClock.CpuMilliseconds();
                var watch = Stopwatch.StartNew();
                await LoadAsync(path, mode, zoom);
                watch.Stop();
                sizes[mode] = source!.DecodedSize!.Value;
                replications[mode] = source.PixelReplication;
                if (iteration >= 0) { samples[mode].Add(watch.Elapsed.TotalMilliseconds); cpu[mode].Add(FrameClock.CpuMilliseconds() - beforeCpu); }
            }
        if (Modes.Where(mode => mode != "native-pixel-duplication" && mode != "webview").Select(mode => sizes[mode]).Distinct().Count() != 1)
            throw new InvalidOperationException("Baseline renderer decode sizes do not match.");
        foreach (var mode in samples.Keys)
        {
            var decoded = sizes[mode];
            report.Cases.Add(new BenchmarkResult("load", Path.GetFileName(path), mode, zoom, -1,
                decoded.Width, decoded.Height, samples[mode].Count, Median(samples[mode]), Percentile(samples[mode], 0.95),
                cpu[mode].Average(), 0, 0, samples[mode].ToArray(),
                OriginalWidth: originals[path].Width, OriginalHeight: originals[path].Height,
                PixelReplication: replications[mode], BitmapBytes: mode == "webview" ? null : (long)decoded.Width * decoded.Height * 4,
                DecodedSizeKnown: mode != "webview", CpuScope: mode == "webview" ? "host-process-only" : "host-process"));
        }
        Save();
    }

    private async Task BenchmarkFramesAsync(string path, string stage, string mode, int round)
    {
        if (mode == "webview") { await BenchmarkWebFramesAsync(path, stage, round); return; }
        status.Text = $"Round {round + 1}: {Path.GetFileName(path)}, {stage}, {mode}";
        await ClearAsync();
        await Task.Delay(200);
        await LoadAsync(path, mode, stage == "zoom-pan" ? 4 : 1);
        await Task.Delay(250);
        for (var frame = 0; frame < 20; frame++) { Transform(mode, stage, frame); await FrameClock.NextAsync(); }
        var startPaints = Interlocked.Read(ref gpuPaints);
        FrameClock.BeginMetrics();
        var startCpu = FrameClock.CpuMilliseconds();
        var start = Stopwatch.GetTimestamp();
        var last = start;
        var intervals = new List<double>();
        var updateTimes = new List<double>();
        for (var frame = 0; frame < 120; frame++)
        {
            var update = Stopwatch.GetTimestamp();
            Transform(mode, stage, frame);
            updateTimes.Add(Stopwatch.GetElapsedTime(update).TotalMilliseconds);
            var now = await FrameClock.NextAsync();
            intervals.Add(Stopwatch.GetElapsedTime(last, now).TotalMilliseconds);
            last = now;
        }
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var cpuTime = FrameClock.CpuMilliseconds() - startCpu;
        var paintedFrames = Interlocked.Read(ref gpuPaints) - startPaints;
        var frameMetrics = FrameClock.EndMetrics(report.RefreshRateHz);
        var decoded = source!.DecodedSize!.Value;
        var deadline = 1000 / Math.Max(1, report.RefreshRateHz);
        report.Cases.Add(new BenchmarkResult(stage, Path.GetFileName(path), mode, stage == "zoom-pan" ? 4 : 1, round,
            decoded.Width, decoded.Height, intervals.Count, Median(intervals), Percentile(intervals, 0.95),
            cpuTime / intervals.Count, intervals.Count / (elapsed / 1000), intervals.Count(value => value > deadline * 1.5),
            intervals.ToArray(), Median(updateTimes), frameMetrics,
            mode == "skia-gpu" ? (int)paintedFrames : null,
            mode == "skia-gpu" ? paintedFrames / (elapsed / 1000) : null,
            mode == "skia-gpu" ? Volatile.Read(ref gpuContextObserved) != 0 : null,
            originals[path].Width, originals[path].Height, source.PixelReplication,
            (long)decoded.Width * decoded.Height * 4));
        Save();
    }

    private void Transform(string mode, string stage, int frame)
    {
        var t = frame * 0.08;
        var zoom = stage == "zoom-pan" ? 2.4 + 0.6 * Math.Sin(t) : 1.04 + 0.04 * Math.Sin(t);
        var x = stage == "zoom-pan" ? viewport.Width * 0.12 * Math.Sin(t * 0.7) : 0;
        var y = stage == "zoom-pan" ? viewport.Height * 0.08 * Math.Cos(t * 0.9) : 0;
        if (mode.StartsWith("native", StringComparison.Ordinal)) { nativeHost.Scale = zoom; nativeHost.TranslationX = x; nativeHost.TranslationY = y; }
        else if (mode == "skia-gpu") skia.SetTransform(zoom, x, y);
        else cpuSkia.SetTransform(zoom, x, y);
    }

    private async Task BenchmarkWebFramesAsync(string path, string stage, int round)
    {
        status.Text = $"Round {round + 1}: {Path.GetFileName(path)}, {stage}, webview";
        await ClearAsync();
        await Task.Delay(200);
        await LoadAsync(path, "webview", stage == "zoom-pan" ? 4 : 1);
        await Task.Delay(250);
        await web.MotionAsync(stage, 20);
        FrameClock.BeginMetrics();
        var startCpu = FrameClock.CpuMilliseconds();
        var motion = await web.MotionAsync(stage, 120);
        var cpu = FrameClock.CpuMilliseconds() - startCpu;
        var metrics = FrameClock.EndMetrics(report.RefreshRateHz);
        var intervals = motion.Intervals.ToList();
        var deadline = 1000 / Math.Max(1, report.RefreshRateHz);
        report.Cases.Add(new BenchmarkResult(stage, Path.GetFileName(path), "webview", stage == "zoom-pan" ? 4 : 1, round,
            motion.Width, motion.Height, intervals.Count, Median(intervals), Percentile(intervals, .95), cpu / intervals.Count,
            intervals.Count / (intervals.Sum() / 1000), intervals.Count(value => value > deadline * 1.5), intervals.ToArray(),
            FrameMetrics: metrics, OriginalWidth: originals[path].Width, OriginalHeight: originals[path].Height,
            DecodedSizeKnown: false, Clock: "browser-requestAnimationFrame", CpuScope: "host-process-only"));
        Save();
    }

    private async Task VerifyGpuSamplingAsync(string path)
    {
        status.Text = "Checking GPU smoothing and pixel magnification…";
        await ClearAsync();
        await LoadAsync(path, "skia-gpu", 4);
        await Task.Delay(200);
        foreach (var check in new[] { (Zoom: 0.25, Name: "DownscaledSmooth"), (Zoom: 1d, Name: "FitSharp"), (Zoom: 3d, Name: "MagnifiedSharp"), (Zoom: 1d, Name: "ResetSharp") })
        {
            var zoom = check.Zoom;
            using var bitmap = await CaptureGpuAsync(zoom);
            var scale = Math.Min(bitmap.Width, bitmap.Height) / 256d * zoom;
            SKColor Sample(double x, double y) => bitmap.GetPixel(
                (int)Math.Floor(bitmap.Width / 2d + (x - 128) * scale),
                (int)Math.Floor(bitmap.Height / 2d + (y - 128) * scale));
            static bool Near(SKColor actual, SKColor expected) =>
                Math.Abs(actual.Red - expected.Red) <= 2 && Math.Abs(actual.Green - expected.Green) <= 2 && Math.Abs(actual.Blue - expected.Blue) <= 2;
            var before = Sample(zoom < 1 ? 143.9 : 143.75, 136);
            var after = Sample(zoom < 1 ? 144.1 : 144.25, 136);
            var correct = zoom < 1
                ? !Near(before, SKColors.OrangeRed) && !Near(before, SKColors.DodgerBlue)
                    && !Near(after, SKColors.OrangeRed) && !Near(after, SKColors.DodgerBlue)
                : Near(before, SKColors.OrangeRed) && Near(after, SKColors.DodgerBlue);
            report.SamplingChecks[check.Name] = correct;
            using var output = File.Create(Path.Combine(OutputDirectory(), $"pixel-{check.Name.ToLowerInvariant()}-gpu.png"));
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            data.SaveTo(output);
            if (!correct) throw new InvalidOperationException($"GPU sampling check failed at zoom {zoom}: {before}, {after}.");
        }
    }

    private async Task<SKBitmap> CaptureGpuAsync(double zoom)
    {
        var ready = new TaskCompletionSource<SKBitmap>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<SKPaintGLSurfaceEventArgs> callback = (_, args) =>
        {
            var bitmap = new SKBitmap(new SKImageInfo(args.Info.Width, args.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (!args.Surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
            { bitmap.Dispose(); ready.TrySetException(new IOException("GPU readback failed.")); }
            else if (!ready.TrySetResult(bitmap)) bitmap.Dispose();
        };
        skia.PaintSurface += callback;
        try
        {
            skia.SetTransform(zoom, 0, 0);
            return await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch { ready.TrySetCanceled(); throw; }
        finally { skia.PaintSurface -= callback; }
    }

    private async Task LoadAsync(string path, string mode, double zoom)
    {
        nativeHost.IsVisible = mode.StartsWith("native", StringComparison.Ordinal);
        skia.IsVisible = mode == "skia-gpu";
        cpuSkia.IsVisible = mode == "skia-cpu";
        web.IsVisible = mode == "webview";
        var target = ViewerImageSizing.Viewport(viewport.Width, viewport.Height, Density(), zoom);
        if (mode == "native")
        {
            // Plain native decoding never enlarges source pixels. Clamp only
            // this request to source bounds so the experiment selects factor 1.
            var original = originals[path];
            target = new(Math.Min(target.Width, original.Width), Math.Min(target.Height, original.Height));
        }
        source = new ViewerImageSource(path, null, target, zoomed: zoom > 1);
        if (mode == "webview")
        {
            var state = await web.LoadAsync(path);
            report.WebViewUserAgent = state.UserAgent;
            report.WebViewDevicePixelRatio = state.Dpr;
            source.SetSizes(new(state.Width, state.Height), new ViewerImageDecodePlan(new(state.Width, state.Height), 1), true);
            source.Complete();
        }
        else if (mode.StartsWith("native", StringComparison.Ordinal)) native.Source = source;
        else if (mode == "skia-gpu") skia.Source = source;
        else cpuSkia.Source = source;
        await source.Ready.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private double Density()
    {
#if WINDOWS
        if (viewport.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement { XamlRoot: { } root }) return root.RasterizationScale;
#endif
        return DeviceDisplay.Current.MainDisplayInfo.Density;
    }

    private async Task ClearAsync()
    {
        await web.ResetAsync();
        if (source is not null) { _ = source.Cancel(); source.ReleaseNativeImage(); }
        native.Source = null;
        skia.Source = null;
        cpuSkia.Source = null;
        source = null;
        nativeHost.Scale = 1;
        nativeHost.TranslationX = nativeHost.TranslationY = 0;
        skia.SetTransform(1, 0, 0);
        cpuSkia.SetTransform(1, 0, 0);
    }

    private static string OutputDirectory()
    {
#if ANDROID
        var directory = Platform.CurrentActivity!.GetExternalFilesDir(null)!.AbsolutePath;
#else
        var directory = Environment.GetEnvironmentVariable("VIEWER_BENCHMARK_OUTPUT") ?? Path.Combine(AppContext.BaseDirectory, "results");
#endif
        Directory.CreateDirectory(directory);
        return directory;
    }

    private void Save() => File.WriteAllText(Path.Combine(OutputDirectory(), "results.json"),
        JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

    private static double Median(List<double> values) => Percentile(values, 0.5);
    private static double Percentile(List<double> values, double percentile)
    {
        var sorted = values.Order().ToArray();
        return sorted[Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1)];
    }

    private sealed class BenchmarkReport
    {
        public string Platform { get; set; } = "";
        public string Device { get; set; } = "";
        public string OperatingSystem { get; set; } = "";
        public double[] ViewportDip { get; set; } = [];
        public double Density { get; set; }
        public double RefreshRateHz { get; set; }
        public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? CompletedUtc { get; set; }
        public string? Error { get; set; }
        public List<BenchmarkResult> Cases { get; } = [];
        public Dictionary<string, bool> SamplingChecks { get; } = [];
        public string WebViewUserAgent { get; set; } = "";
        public double WebViewDevicePixelRatio { get; set; }
    }

    private sealed record BenchmarkResult(string Stage, string Fixture, string Renderer, double TargetZoom, int Round,
        int DecodedWidth, int DecodedHeight, int Samples, double MedianMs, double P95Ms, double CpuMsPerSample,
        double UpdatesPerSecond, int MissedDeadlines, double[] RawMilliseconds, double MedianUpdateSubmissionMs = 0,
        FrameStatistics? FrameMetrics = null, int? PaintedFrames = null, double? PaintsPerSecond = null,
        bool? GpuContextObserved = null, int OriginalWidth = 0, int OriginalHeight = 0,
        int PixelReplication = 1, long? BitmapBytes = null, bool DecodedSizeKnown = true,
        string Clock = "platform-ui-callback", string CpuScope = "host-process");
}


