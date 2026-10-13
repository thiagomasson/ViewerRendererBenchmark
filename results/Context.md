# Standalone benchmark run context

Renderer Benchmark is an independent MAUI application with locally included native, native pixel-duplication, CPU/GPU Skia, and WebView implementations. Source fingerprints refer only to files within this app. No source or project dependency is external to the app directory.

Build settings: .NET SDK 10.0.401, MAUI 10.0.110, SkiaSharp 4.153.1; optimized Release, AOT and trimming disabled. Builds are sequential and measurement begins after compilation finishes.

Windows device: Intel Core i5-10400F and NVIDIA GeForce RTX 3060. Android device: Galaxy A53 SM-A536E. Actual OS, viewport, density, refresh rate, WebView user agent, device-pixel ratio, timestamps and raw measurements are saved per platform.

Five rendering modes share generated fixtures and transforms. Native/Skia modes have explicit 8/24 Mi-pixel limits. The browser displays original encoded files; its decoder allocation is unknown, its cadence uses JavaScript requestAnimationFrame, and browser subprocess CPU is not counted by the host-process timer. Browser readiness includes base64 transfer and image.decode(). Loading timing does not establish first-presentation latency.

The app prepares decode/replication before interaction measurements. Dynamic resolution and replication upgrades during a gesture are excluded. Native interpolation remains enabled after pixel duplication. GPU sampling is validated after timing, with readbacks excluded from measurements. Browser filtering follows CSS behavior, which can differ at fractional scales.

These results do not measure power, peak memory, GIF/video playback, or full application navigation. Historical measurements remain separate from the current dataset.
