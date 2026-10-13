using System.Text.Json;

namespace ViewerRendererBenchmark;

internal sealed class WebViewer : WebView
{
    private readonly TaskCompletionSource initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool started;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal WebViewer()
    {
        InputTransparent = true;
        Navigated += (_, args) =>
        {
            if (args.Result == WebNavigationResult.Success) initialized.TrySetResult();
            else initialized.TrySetException(new IOException($"WebView navigation failed: {args.Result}"));
        };
    }

    internal async Task<WebState> LoadAsync(string path)
    {
        if (!started)
        {
            started = true;
            using var input = await FileSystem.OpenAppPackageFileAsync("viewer.html");
            using var reader = new StreamReader(input);
            Source = new HtmlWebViewSource { Html = await reader.ReadToEndAsync() };
        }
        await initialized.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var encoded = await Task.Run(() => Convert.ToBase64String(File.ReadAllBytes(path)));
        var mime = Path.GetExtension(path) == ".jpg" ? "image/jpeg" : "image/png";
        var url = JsonSerializer.Serialize($"data:{mime};base64,{encoded}");
        await EvaluateJavaScriptAsync($"rendererBench.load({url})");
        return await WaitAsync(state => state.Loaded);
    }

    internal async Task<WebState> MotionAsync(string stage, int count)
    {
        await EvaluateJavaScriptAsync($"rendererBench.motion({JsonSerializer.Serialize(stage)},{count})");
        return await WaitAsync(state => state.Done);
    }

    internal async Task ResetAsync()
    {
        if (initialized.Task.IsCompletedSuccessfully) await EvaluateJavaScriptAsync("rendererBench.reset()");
    }

    private async Task<WebState> WaitAsync(Func<WebState, bool> finished)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var value = await EvaluateJavaScriptAsync("rendererBench.state()");
            using var json = JsonDocument.Parse(value);
            var text = json.RootElement.ValueKind == JsonValueKind.String ? json.RootElement.GetString()! : value;
            var state = JsonSerializer.Deserialize<WebState>(text, JsonOptions)!;
            if (state.Error is not null) throw new IOException(state.Error);
            if (finished(state)) return state;
            await Task.Delay(25);
        }
        throw new TimeoutException("WebView benchmark timed out.");
    }
}

internal sealed class WebState
{
    public bool Loaded { get; set; }
    public bool Done { get; set; }
    public string? Error { get; set; }
    public double[] Intervals { get; set; } = [];
    public int Width { get; set; }
    public int Height { get; set; }
    public double Dpr { get; set; }
    public string UserAgent { get; set; } = "";
}
