using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace ViewerRendererBenchmark;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, Exported = true,
    Name = "com.viewerrendererbenchmark.app.MainActivity",
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public sealed class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        if (Window is not null) FrameClock.Attach(Window);
    }
}

