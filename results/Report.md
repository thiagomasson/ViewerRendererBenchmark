# Measured viewer rendering performance

Native recorded lower median load readiness latency than CPU Skia in 8 of 12 load cases. Interaction cadence, GPU paint callbacks, CPU time, and Android frame durations appear below.

These tables use the current results.json from each platform. Loading is readiness latency (lower is better). Interaction speed is UI update cadence (higher is better), rather than a separate measurement of displayed FPS. Interaction numbers average the two rounds. Historical CPU-only measurements are preserved separately.

This standalone app compares five self-contained renderers. Plain native disables replication by clamping its request to source bounds. The browser mode displays original encoded images with CSS transforms and adaptive image-rendering; it does not enforce the native decode budgets. See [context](Context.md), [source hashes](sources.json), and [reproduction](../README.md).

## windows

Device: System Product Name. OS: 10.0.26200.9457. Viewport: 1060.0 × 754.0 DIP; density 1; refresh rate 60 Hz. Run: 2026-10-13T13:58:58.0447070-03:00 to 2026-10-13T14:02:50.4530112-03:00.
Browser: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36 Edg/154.0.0.0. Browser device pixel ratio: 1.

### Loading

| Fixture | Target zoom | Native median ms | Native duplication median ms | CPU Skia median ms | GPU Skia median ms | WebView median ms |
|---|---:|---:|---:|---:|---:|---:|
| photo-12mp.jpg | 1 | 31.4 | 29.0 | 77.3 | 59.6 | 125.9 |
| photo-12mp.jpg | 4 | 55.8 | 47.4 | 115.8 | 94.1 | 126.4 |
| photo-12mp.png | 1 | 93.9 | 90.8 | 159.8 | 166.2 | 299.0 |
| photo-12mp.png | 4 | 80.8 | 70.8 | 229.3 | 190.5 | 302.5 |
| pixel-256.png | 1 | 10.9 | 5.1 | 2.8 | 2.6 | 32.2 |
| pixel-256.png | 4 | 11.2 | 36.9 | 6.7 | 7.3 | 24.5 |

### Duplication allocation

| Fixture | Target zoom | Replication | Plain bitmap | Duplicated bitmap | Plain / duplicated MiB | Plain / duplicated load CPU ms |
|---|---:|---:|---|---|---:|---:|
| photo-12mp.jpg | 1 | 1× | 1005 × 754 | 1005 × 754 | 2.9 / 2.9 | 52.1 / 39.1 |
| photo-12mp.jpg | 4 | 1× | 4000 × 3000 | 4000 × 3000 | 45.8 / 45.8 | 78.1 / 62.5 |
| photo-12mp.png | 1 | 1× | 1005 × 754 | 1005 × 754 | 2.9 / 2.9 | 109.4 / 117.2 |
| photo-12mp.png | 4 | 1× | 4000 × 3000 | 4000 × 3000 | 45.8 / 45.8 | 101.6 / 109.4 |
| pixel-256.png | 1 | 3× | 256 × 256 | 768 × 768 | 0.3 / 2.3 | 10.4 / 5.2 |
| pixel-256.png | 4 | 12× | 256 × 256 | 3072 × 3072 | 0.3 / 36.0 | 10.4 / 39.1 |

### Interaction

| Fixture | Movement | Native updates/s | Duplication updates/s | CPU Skia updates/s | GPU Skia updates/s | WebView RAF/s | GPU paint callbacks/s | Native / duplication / CPU / GPU / WebView host CPU ms/update |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| photo-12mp.jpg | fit-motion | 59.8 | 59.8 | 56.9 | 59.6 | 60.0 | 59.6 | 2.8 / 2.7 / 17.9 / 1.4 / 1.0 |
| photo-12mp.jpg | zoom-pan | 59.8 | 59.8 | 36.9 | 59.5 | 60.0 | 59.5 | 2.9 / 2.7 / 27.9 / 2.5 / 0.6 |
| photo-12mp.png | fit-motion | 59.8 | 59.8 | 51.5 | 59.8 | 60.0 | 59.8 | 2.7 / 3.1 / 19.9 / 2.0 / 1.0 |
| photo-12mp.png | zoom-pan | 59.8 | 59.8 | 36.7 | 59.6 | 60.0 | 59.6 | 2.0 / 2.1 / 28.1 / 1.8 / 1.8 |
| pixel-256.png | fit-motion | 59.8 | 59.8 | 59.8 | 59.5 | 60.0 | 59.5 | 2.6 / 2.2 / 4.6 / 2.5 / 0.8 |
| pixel-256.png | zoom-pan | 59.8 | 59.8 | 59.8 | 59.5 | 60.0 | 59.5 | 2.3 / 2.9 / 5.1 / 2.5 / 0.5 |

Validation passed: all 90 cases, matching baseline dimensions, valid integer-expanded duplication sizes, pixel limits, and GPU context/paint/downscale/fit/zoom/reset checks. Android requires at least 100 window frame metrics per native/CPU case and a nonempty metric set per asynchronous GPU case.

## android

Device: SM-A536E. OS: 16. Viewport: 387.4 × 777.0 DIP; density 2.625; refresh rate 60 Hz. Run: 2026-10-13T13:59:01.9284974-03:00 to 2026-10-13T14:03:39.5248344-03:00.
Browser: Mozilla/5.0 (Linux; Android 16; SM-A536E Build/BP2A.250605.031.A3; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/154.0.8037.57 Mobile Safari/537.36. Browser device pixel ratio: 2.625.

### Loading

| Fixture | Target zoom | Native median ms | Native duplication median ms | CPU Skia median ms | GPU Skia median ms | WebView median ms |
|---|---:|---:|---:|---:|---:|---:|
| photo-12mp.jpg | 1 | 79.0 | 77.0 | 97.3 | 66.4 | 125.8 |
| photo-12mp.jpg | 4 | 111.8 | 111.3 | 223.4 | 168.3 | 132.1 |
| photo-12mp.png | 1 | 96.9 | 95.0 | 126.6 | 81.7 | 217.6 |
| photo-12mp.png | 4 | 128.5 | 127.8 | 252.7 | 184.7 | 235.2 |
| pixel-256.png | 1 | 42.9 | 34.6 | 33.1 | 16.2 | 34.7 |
| pixel-256.png | 4 | 41.2 | 107.1 | 38.9 | 17.6 | 42.4 |

### Duplication allocation

| Fixture | Target zoom | Replication | Plain bitmap | Duplicated bitmap | Plain / duplicated MiB | Plain / duplicated load CPU ms |
|---|---:|---:|---|---|---:|---:|
| photo-12mp.jpg | 1 | 1× | 1016 × 762 | 1016 × 762 | 3.0 / 3.0 | 82.8 / 83.2 |
| photo-12mp.jpg | 4 | 1× | 4000 × 3000 | 4000 × 3000 | 45.8 / 45.8 | 117.0 / 117.7 |
| photo-12mp.png | 1 | 1× | 1016 × 762 | 1016 × 762 | 3.0 / 3.0 | 99.5 / 94.3 |
| photo-12mp.png | 4 | 1× | 4000 × 3000 | 4000 × 3000 | 45.8 / 45.8 | 135.8 / 132.3 |
| pixel-256.png | 1 | 4× | 256 × 256 | 1024 × 1024 | 0.3 / 4.0 | 52.8 / 53.2 |
| pixel-256.png | 4 | 16× | 256 × 256 | 4096 × 4096 | 0.3 / 64.0 | 37.2 / 126.7 |

### Interaction

| Fixture | Movement | Native updates/s | Duplication updates/s | CPU Skia updates/s | GPU Skia updates/s | WebView RAF/s | GPU paint callbacks/s | Native / duplication / CPU / GPU / WebView host CPU ms/update |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| photo-12mp.jpg | fit-motion | 59.9 | 59.8 | 36.2 | 59.8 | 59.8 | 59.8 | 12.8 / 14.4 / 31.2 / 19.3 / 20.0 |
| photo-12mp.jpg | zoom-pan | 59.8 | 59.9 | 13.6 | 59.8 | 59.3 | 59.8 | 16.3 / 15.7 / 77.6 / 18.6 / 20.1 |
| photo-12mp.png | fit-motion | 59.8 | 59.9 | 36.5 | 59.8 | 59.8 | 59.8 | 15.5 / 15.5 / 31.3 / 18.8 / 19.0 |
| photo-12mp.png | zoom-pan | 59.8 | 59.8 | 13.7 | 59.8 | 59.6 | 59.8 | 15.7 / 15.6 / 77.3 / 18.3 / 20.6 |
| pixel-256.png | fit-motion | 59.8 | 59.8 | 59.5 | 59.8 | 59.6 | 59.8 | 15.0 / 14.8 / 19.8 / 19.1 / 20.5 |
| pixel-256.png | zoom-pan | 59.8 | 59.8 | 59.7 | 59.9 | 59.3 | 59.9 | 15.2 / 15.1 / 18.3 / 18.6 / 20.4 |

### Android frame duration

| Fixture | Movement | Native median / p95 ms | Duplication median / p95 ms | CPU Skia median / p95 ms | GPU Skia median / p95 ms | WebView window median / p95 ms |
|---|---|---:|---:|---:|---:|---:|
| photo-12mp.jpg | fit-motion | 10.9 / 13.0 | 11.6 / 13.6 | 38.1 / 42.4 | 11.4 / 15.1 | 19.4 / 29.4 |
| photo-12mp.jpg | zoom-pan | 14.5 / 17.4 | 14.5 / 17.9 | 104.5 / 137.7 | 11.3 / 15.1 | 21.6 / 28.9 |
| photo-12mp.png | fit-motion | 12.0 / 13.7 | 12.0 / 14.4 | 38.7 / 43.1 | 10.8 / 14.7 | 18.8 / 30.0 |
| photo-12mp.png | zoom-pan | 14.2 / 17.4 | 14.4 / 17.8 | 104.3 / 140.2 | 10.4 / 14.2 | 20.7 / 26.2 |
| pixel-256.png | fit-motion | 12.2 / 13.9 | 12.4 / 14.5 | 16.5 / 20.7 | 11.8 / 15.3 | 18.8 / 32.6 |
| pixel-256.png | zoom-pan | 12.7 / 14.9 | 12.8 / 15.9 | 16.4 / 19.7 | 10.7 / 14.1 | 18.3 / 22.0 |

Dropped metric callbacks: 0.

Validation passed: all 90 cases, matching baseline dimensions, valid integer-expanded duplication sizes, pixel limits, and GPU context/paint/downscale/fit/zoom/reset checks. Android requires at least 100 window frame metrics per native/CPU case and a nonempty metric set per asynchronous GPU case.

## Interpretation and limits

WebView motion runs entirely in JavaScript with requestAnimationFrame and CSS transforms. RAF cadence is not proof of displayed FPS. WebView CPU numbers measure only the embedding app process and exclude isolated browser processes; do not use them to conclude lower total CPU or battery use. Browser dimensions are intrinsic image dimensions, not measured decoded allocations; browser bitmap/GPU memory is unknown. Readiness includes fixture reads, base64 transfer, image loading, and image.decode(). The viewer switches CSS image-rendering between auto and pixelated at 2 physical pixels per source pixel. Fractional-scale filtering may differ from Skia nearest-neighbor.
Both native modes transform the same Image control. Pixel duplication prepares an integer-expanded bitmap first; its pixel storage grows quadratically with the replication factor. Interaction cases predecode at target zoom 1 or 4, excluding re-decoding when replication factors change during a gesture. Bitmap MiB is width × height × 4, not peak process/GPU memory. Native filtering remains active after duplication, so this is not pixel-perfect equivalence to nearest-neighbor Skia at every fractional scale. CPU/GPU Skia share bounded decoding and pixel copying. Readiness excludes first presentation and GPU texture upload; GPU callbacks precede presentation. Android window frame metrics do not separately time the GPU rendering thread.

The results apply to these implementations, generated fixtures, devices and viewport sizes. Real GIF/video playback, gesture recognition, navigation, original-resolution reloads while zooming, power consumption and memory peaks were not benchmarked. Small-image sampling differs: Skia exercises nearest-neighbor magnification, while native uses its default filter. Process CPU includes benchmark/framework overhead, and the six-load p95 is the maximum observed sample. The CPU control is retained from the previous viewer and shares the new persistent image object with GPU Skia. Historical CPU-only results are preserved in cpu-baseline/.
