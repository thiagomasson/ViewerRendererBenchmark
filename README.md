# Viewer Renderer Benchmark

A self-contained .NET MAUI app for Android and Windows. Open ViewerRenderer.slnx or build ViewerRenderer.csproj. All rendering implementations, decoders, fixtures, assets, and analysis scripts are included locally. No parent project, external source links, repository history, or machine-specific package directory is required.

Application title: Viewer Renderer Benchmark. Android application ID: com.viewerrendererbenchmark.app.

## Renderers

1. Native Image, bounded decode without enlargement.
2. Native Image with integer pixel duplication when source pixels cover at least two physical pixels, subject to the bitmap budgets.
3. CPU Skia using SKCanvasView.
4. GPU Skia using SKGLView and on-demand redraws.
5. WebView using an img element, JavaScript requestAnimationFrame, CSS transforms, and adaptive image-rendering.

The browser receives original encoded fixtures as data URLs. Loading includes file reading, base64 transfer, browser loading, and image.decode(). Browser decoded memory is not exposed, and the native 8/24 Mi-pixel decode caps are not applied. Reported browser dimensions are intrinsic image dimensions, not measured decoded bitmap allocations. Motion runs inside JavaScript; no .NET call is made for each transform. Polling reads completion state every 25 ms.

## Protocol

Generated fixtures: 4000 × 3000 JPEG/PNG images and a 256 × 256 checkerboard PNG. Each renderer/target gets two warm-up and six measured loads; order rotates. Two interaction rounds use opposite renderer order, with 20 warm-up and 120 measured updates per case. Fitted motion scales between 1 and 1.08. Zoom/pan scales between 1.8 and 3 with horizontal/vertical movement. Native/Skia cases preload target zoom 1 or 4, excluding decode upgrades during motion.

CPU/GPU/native callbacks measure UI update cadence. Browser motion measures requestAnimationFrame cadence; neither proves presented FPS. GPU paints are counted separately before presentation. Android window frame durations are recorded but do not independently measure browser process or GPU rendering thread work. CPU time covers the embedding app process; browser subprocess CPU is excluded, so its host CPU numbers cannot establish lower total CPU or battery use.

The native duplication factor, bitmap dimensions, and four-byte pixel storage are recorded. This excludes temporary decode buffers and GPU copies. Native interpolation remains enabled after duplication. Skia uses nearest sampling at at least two physical pixels per original pixel and smooth sampling below that. After timing, GPU readback checks downscale, fit, magnification, and reset behavior.

The analyzer validates 90 cases per platform, sample counts, three matching bounded baseline resolutions, integer replication and limits, browser intrinsic metadata, GPU paint callbacks, and sampling checks. It writes results/summary.json and results/Report.md. All raw intervals remain in results.json.

## Build and run

Install .NET 10 with the MAUI Windows/Android workloads, the Android SDK/JDK, and WebView2 Runtime on Windows. NuGet dependencies restore from nuget.org; optionally use your normal local package cache. Builds use optimized Release with AOT and trimming disabled. Build platforms sequentially.

Run these commands from this project's directory:

```powershell
dotnet build ViewerRenderer.csproj -c Release -f net10.0-windows10.0.19041.0
$env:VIEWER_BENCHMARK_OUTPUT = Join-Path $PWD 'results\windows'
$binaryDirectory = Join-Path $PWD 'bin\Release\net10.0-windows10.0.19041.0\win-x64'
Start-Process -FilePath (Join-Path $binaryDirectory 'ViewerRenderer.exe') -WorkingDirectory $binaryDirectory -WindowStyle Normal
```

Keep the window visible and do not resize it during a run. Close it before rerunning.

```powershell
dotnet build ViewerRenderer.csproj -c Release -f net10.0-android -t:Rebuild
$adb = Join-Path $env:ANDROID_HOME 'platform-tools\adb.exe'
& $adb devices
$deviceSerial = 'YOUR_DEVICE_SERIAL'
& $adb -s $deviceSerial install --no-incremental -r bin\Release\net10.0-android\android-arm64\com.viewerrendererbenchmark.app-Signed.apk
& $adb -s $deviceSerial shell am start -n com.viewerrendererbenchmark.app/com.viewerrendererbenchmark.app.MainActivity
```

Unlock the phone and keep the app foreground. It keeps the screen on while active. When complete:

```powershell
New-Item -ItemType Directory -Force results\android | Out-Null
& $adb -s $deviceSerial pull /sdcard/Android/data/com.viewerrendererbenchmark.app/files/results.json results\android\results.json
& .\Analyze.ps1
& $adb -s $deviceSerial shell am force-stop com.viewerrendererbenchmark.app
```

Set AndroidSdkDirectory and JavaSdkDirectory as build properties if automatic detection does not match your installation. Rebuild Android after changing source; the included runtime configuration is most reliably tested with a full non-incremental package installation.

results/Context.md describes the saved run environment; results/sources.json fingerprints the app's local source files. Previous datasets are historical measurements and do not use the current five-mode analyzer.
