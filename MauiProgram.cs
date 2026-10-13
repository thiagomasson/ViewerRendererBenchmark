using ViewerRendererBenchmark.Services;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace ViewerRendererBenchmark;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<BenchmarkApp>().UseSkiaSharp();
        builder.Services.AddSingleton<ViewerImageSourceService>();
        builder.Services.AddSingleton<SkiaViewerImageSourceService>();
        builder.ConfigureImageSources(sources => sources.AddService<ViewerRendererBenchmark.Infrastructure.ViewerImageSource>(
            services => services.GetRequiredService<ViewerImageSourceService>()));
        return builder.Build();
    }
}

public sealed class BenchmarkApp : Application
{
    protected override Window CreateWindow(IActivationState? state) => new(new BenchmarkPage())
    {
        Title = "Renderer Benchmark", Width = 1100, Height = 850
    };
}

