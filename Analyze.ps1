param([string]$ResultsDirectory = (Join-Path $PSScriptRoot 'results'))

$ErrorActionPreference = 'Stop'
$summary = @()
foreach ($platform in @('windows', 'android')) {
    $path = Join-Path $ResultsDirectory "$platform/results.json"
    $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($report.Error -or !$report.CompletedUtc) { throw "$platform benchmark did not complete: $($report.Error)" }
    $modes = @($report.Cases.Renderer | Sort-Object -Unique)
    if ($modes.Count -ne 5 -or @($modes | Where-Object { $_ -notin @('native','native-pixel-duplication','skia-cpu','skia-gpu','webview') }).Count) { throw "${platform}: invalid renderer set" }
    $expectedCases = 18 * $modes.Count
    if ($report.Cases.Count -ne $expectedCases) { throw "${platform}: expected $expectedCases cases, got $($report.Cases.Count)" }
    if ($report.Density -le 0 -or $report.RefreshRateHz -le 0) { throw "${platform}: invalid display information" }
    foreach ($check in @('DownscaledSmooth','FitSharp','MagnifiedSharp','ResetSharp')) {
        if (!$report.SamplingChecks.$check) { throw "${platform}: GPU output check failed: $check" }
    }

    $keys = @{}
    foreach ($case in $report.Cases) {
        $key = "$($case.Stage)/$($case.Fixture)/$($case.TargetZoom)/$($case.Round)/$($case.Renderer)"
        if ($keys.ContainsKey($key)) { throw "Duplicate case: $key" }
        $keys[$key] = $true
        $expected = if ($case.Stage -eq 'load') { 6 } else { 120 }
        if ($case.Samples -ne $expected -or $case.RawMilliseconds.Count -ne $expected) { throw "Missing samples: $key" }
        if ($case.MedianMs -le 0 -or $case.P95Ms -lt $case.MedianMs -or $case.CpuMsPerSample -lt 0) { throw "Invalid timings: $key" }
        if (@($case.RawMilliseconds | Where-Object { $_ -le 0 -or [double]::IsNaN($_) -or [double]::IsInfinity($_) }).Count) { throw "Invalid raw sample: $key" }
        $pixelCap = if ($case.TargetZoom -gt 1) { 24 * 1024 * 1024 } else { 8 * 1024 * 1024 }
        if ($case.DecodedWidth -le 0 -or $case.DecodedHeight -le 0 -or ($case.DecodedSizeKnown -and $case.DecodedWidth * $case.DecodedHeight -gt $pixelCap)) { throw "Invalid decoded dimensions: $key" }
        if ($case.Renderer -eq 'webview') {
            if ($case.DecodedSizeKnown -or $null -ne $case.BitmapBytes -or $case.DecodedWidth -ne $case.OriginalWidth -or $case.DecodedHeight -ne $case.OriginalHeight) { throw "Incorrect browser metadata: $key" }
        } elseif ($case.BitmapBytes -ne [long]$case.DecodedWidth * $case.DecodedHeight * 4) { throw "Incorrect bitmap byte count: $key" }
        if ($case.PixelReplication -gt 1) {
            if ($case.Renderer -ne 'native-pixel-duplication' -or $case.DecodedWidth -ne $case.OriginalWidth * $case.PixelReplication -or $case.DecodedHeight -ne $case.OriginalHeight * $case.PixelReplication -or [math]::Max($case.DecodedWidth,$case.DecodedHeight) -gt 16384) { throw "Invalid pixel duplication: $key" }
        } elseif ($case.DecodedWidth -gt $case.OriginalWidth -or $case.DecodedHeight -gt $case.OriginalHeight) { throw "Unexpected enlarged bitmap: $key" }
        if ($case.Fixture -eq 'pixel-256.png' -and $case.Renderer -eq 'native-pixel-duplication' -and $case.PixelReplication -lt 2) { throw "Duplication path was not exercised: $key" }
        $minimumMetrics = if ($case.Renderer -in @('skia-gpu','webview')) { 1 } else { 100 }
        if ($platform -eq 'android' -and $case.Stage -ne 'load' -and (!$case.FrameMetrics -or $case.FrameMetrics.Samples -lt $minimumMetrics)) { throw "Insufficient Android frame metrics: $key" }
        if ($case.Renderer -eq 'skia-gpu' -and $case.Stage -ne 'load' -and (!$case.GpuContextObserved -or $case.PaintedFrames -le 0 -or $case.PaintsPerSecond -le 0)) { throw "GPU surface did not paint: $key" }
    }
    foreach ($pair in ($report.Cases | Group-Object Stage, Fixture, TargetZoom, Round)) {
        if ($pair.Count -ne $modes.Count -or @($pair.Group.Renderer | Sort-Object -Unique).Count -ne $modes.Count) { throw "Missing renderer comparison: $($pair.Name)" }
        $baseline = @($pair.Group | Where-Object { $_.Renderer -notin @('native-pixel-duplication','webview') })
        if (@($baseline.DecodedWidth | Sort-Object -Unique).Count -ne 1 -or @($baseline.DecodedHeight | Sort-Object -Unique).Count -ne 1) { throw "Unequal baseline dimensions: $($pair.Name)" }
        $dup = $pair.Group | Where-Object Renderer -eq 'native-pixel-duplication'
        if ($dup.PixelReplication -eq 1 -and ($dup.DecodedWidth -ne $baseline[0].DecodedWidth -or $dup.DecodedHeight -ne $baseline[0].DecodedHeight)) { throw "Nonreplicated decode differs: $($pair.Name)" }
    }

    foreach ($group in ($report.Cases | Group-Object Stage, Fixture, Renderer, TargetZoom)) {
        $first = $group.Group[0]
        $summary += [pscustomobject]@{
            Platform = $platform
            Stage = $first.Stage
            Fixture = $first.Fixture
            Renderer = $first.Renderer
            TargetZoom = $first.TargetZoom
            DecodedWidth = $first.DecodedWidth
            DecodedHeight = $first.DecodedHeight
            PixelReplication = $first.PixelReplication
            BitmapBytes = $first.BitmapBytes
            DecodedSizeKnown = $first.DecodedSizeKnown
            Clock = $first.Clock
            CpuScope = $first.CpuScope
            Samples = ($group.Group.Samples | Measure-Object -Sum).Sum
            # Average the two round medians; retain each round and all raw samples in results.json.
            MedianMs = ($group.Group.MedianMs | Measure-Object -Average).Average
            P95Ms = ($group.Group.P95Ms | Measure-Object -Average).Average
            CpuMsPerSample = ($group.Group.CpuMsPerSample | Measure-Object -Average).Average
            UpdatesPerSecond = ($group.Group.UpdatesPerSecond | Measure-Object -Average).Average
            MissedDeadlines = ($group.Group.MissedDeadlines | Measure-Object -Sum).Sum
            FrameMedianMs = if ($first.FrameMetrics) { ($group.Group.FrameMetrics.MedianMs | Measure-Object -Average).Average } else { $null }
            FrameP95Ms = if ($first.FrameMetrics) { ($group.Group.FrameMetrics.P95Ms | Measure-Object -Average).Average } else { $null }
            FrameSamples = if ($first.FrameMetrics) { ($group.Group.FrameMetrics.Samples | Measure-Object -Sum).Sum } else { $null }
            FrameMissedDeadlines = if ($first.FrameMetrics) { ($group.Group.FrameMetrics.MissedDeadlines | Measure-Object -Sum).Sum } else { $null }
            DroppedMetricCallbacks = if ($first.FrameMetrics) { ($group.Group.FrameMetrics.DroppedMetricCallbacks | Measure-Object -Sum).Sum } else { $null }
            PaintedFrames = if ($first.Renderer -eq 'skia-gpu' -and $first.Stage -ne 'load') { ($group.Group.PaintedFrames | Measure-Object -Sum).Sum } else { $null }
            PaintsPerSecond = if ($first.Renderer -eq 'skia-gpu' -and $first.Stage -ne 'load') { ($group.Group.PaintsPerSecond | Measure-Object -Average).Average } else { $null }
        }
    }
    Write-Host "$platform passed: $expectedCases cases; bounded replication; $(36 * $modes.Count) measured loads; $(1440 * $modes.Count) interaction samples."
}
$summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'summary.json') -Encoding utf8
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# Measured viewer rendering performance')
$lines.Add('')
$loadPairs = @($summary | Where-Object Stage -eq load | Group-Object Platform, Fixture, TargetZoom)
$nativeFasterLoads = @($loadPairs | Where-Object {
    ($_.Group | Where-Object Renderer -eq native).MedianMs -lt ($_.Group | Where-Object Renderer -eq skia-cpu).MedianMs
}).Count
$lines.Add("Native recorded lower median load readiness latency than CPU Skia in $nativeFasterLoads of $($loadPairs.Count) load cases. Interaction cadence, GPU paint callbacks, CPU time, and Android frame durations appear below.")
$lines.Add('')
$lines.Add('These tables use the current results.json from each platform. Loading is readiness latency (lower is better). Interaction speed is UI update cadence (higher is better), rather than a separate measurement of displayed FPS. Interaction numbers average the two rounds. Historical CPU-only measurements are preserved separately.')
$lines.Add('')
$lines.Add('This standalone app compares five self-contained renderers. Plain native disables replication by clamping its request to source bounds. The browser mode displays original encoded images with CSS transforms and adaptive image-rendering; it does not enforce the native decode budgets. See [context](Context.md), [source hashes](sources.json), and [reproduction](../README.md).')
$lines.Add('')
function Format-Number([double]$Value) { $Value.ToString('0.0', [System.Globalization.CultureInfo]::InvariantCulture) }
foreach ($platform in @('windows', 'android')) {
    $report = Get-Content -LiteralPath (Join-Path $ResultsDirectory "$platform/results.json") -Raw | ConvertFrom-Json
    $lines.Add("## $platform")
    $lines.Add('')
    $lines.Add("Device: $($report.Device). OS: $($report.OperatingSystem). Viewport: $(Format-Number $report.ViewportDip[0]) × $(Format-Number $report.ViewportDip[1]) DIP; density $($report.Density); refresh rate $($report.RefreshRateHz) Hz. Run: $($report.StartedUtc.ToString('o')) to $($report.CompletedUtc.ToString('o')).")
    $lines.Add("Browser: $($report.WebViewUserAgent). Browser device pixel ratio: $($report.WebViewDevicePixelRatio).")
    $lines.Add('')
    $lines.Add('### Loading')
    $lines.Add('')
    $lines.Add('| Fixture | Target zoom | Native median ms | Native duplication median ms | CPU Skia median ms | GPU Skia median ms | WebView median ms |')
    $lines.Add('|---|---:|---:|---:|---:|---:|---:|')
    foreach ($group in ($summary | Where-Object { $_.Platform -eq $platform -and $_.Stage -eq 'load' } | Group-Object Fixture, TargetZoom)) {
        $native = $group.Group | Where-Object Renderer -eq native
        $dup = $group.Group | Where-Object Renderer -eq 'native-pixel-duplication'
        $skia = $group.Group | Where-Object Renderer -eq skia-cpu
        $gpu = $group.Group | Where-Object Renderer -eq skia-gpu
        $gpuLoad = if ($gpu) { Format-Number $gpu.MedianMs } else { '—' }
        $browser = $group.Group | Where-Object Renderer -eq webview
        $lines.Add("| $($native.Fixture) | $($native.TargetZoom) | $(Format-Number $native.MedianMs) | $(Format-Number $dup.MedianMs) | $(Format-Number $skia.MedianMs) | $gpuLoad | $(Format-Number $browser.MedianMs) |")
    }
    $lines.Add(''); $lines.Add('### Duplication allocation'); $lines.Add('')
    $lines.Add('| Fixture | Target zoom | Replication | Plain bitmap | Duplicated bitmap | Plain / duplicated MiB | Plain / duplicated load CPU ms |')
    $lines.Add('|---|---:|---:|---|---|---:|---:|')
    foreach ($group in ($summary | Where-Object { $_.Platform -eq $platform -and $_.Stage -eq 'load' } | Group-Object Fixture,TargetZoom)) {
        $native = $group.Group | Where-Object Renderer -eq native
        $dup = $group.Group | Where-Object Renderer -eq 'native-pixel-duplication'
        $lines.Add("| $($dup.Fixture) | $($dup.TargetZoom) | $($dup.PixelReplication)× | $($native.DecodedWidth) × $($native.DecodedHeight) | $($dup.DecodedWidth) × $($dup.DecodedHeight) | $(Format-Number ($native.BitmapBytes / 1MB)) / $(Format-Number ($dup.BitmapBytes / 1MB)) | $(Format-Number $native.CpuMsPerSample) / $(Format-Number $dup.CpuMsPerSample) |")
    }
    $lines.Add('')
    $lines.Add('### Interaction')
    $lines.Add('')
    $lines.Add('| Fixture | Movement | Native updates/s | Duplication updates/s | CPU Skia updates/s | GPU Skia updates/s | WebView RAF/s | GPU paint callbacks/s | Native / duplication / CPU / GPU / WebView host CPU ms/update |')
    $lines.Add('|---|---|---:|---:|---:|---:|---:|---:|---:|')
    foreach ($group in ($summary | Where-Object { $_.Platform -eq $platform -and $_.Stage -ne 'load' } | Group-Object Fixture, Stage)) {
        $native = $group.Group | Where-Object Renderer -eq native
        $skia = $group.Group | Where-Object Renderer -eq skia-cpu
        $gpu = $group.Group | Where-Object Renderer -eq skia-gpu
        $gpuRate = if ($gpu) { Format-Number $gpu.UpdatesPerSecond } else { '—' }
        $gpuPaintRate = if ($gpu) { Format-Number $gpu.PaintsPerSecond } else { '—' }
        $gpuCpu = if ($gpu) { Format-Number $gpu.CpuMsPerSample } else { '—' }
        $dup = $group.Group | Where-Object Renderer -eq 'native-pixel-duplication'
        $browser = $group.Group | Where-Object Renderer -eq webview
        $lines.Add("| $($native.Fixture) | $($native.Stage) | $(Format-Number $native.UpdatesPerSecond) | $(Format-Number $dup.UpdatesPerSecond) | $(Format-Number $skia.UpdatesPerSecond) | $gpuRate | $(Format-Number $browser.UpdatesPerSecond) | $gpuPaintRate | $(Format-Number $native.CpuMsPerSample) / $(Format-Number $dup.CpuMsPerSample) / $(Format-Number $skia.CpuMsPerSample) / $gpuCpu / $(Format-Number $browser.CpuMsPerSample) |")
    }
    if ($platform -eq 'android') {
        $lines.Add('')
        $lines.Add('### Android frame duration')
        $lines.Add('')
        $lines.Add('| Fixture | Movement | Native median / p95 ms | Duplication median / p95 ms | CPU Skia median / p95 ms | GPU Skia median / p95 ms | WebView window median / p95 ms |')
        $lines.Add('|---|---|---:|---:|---:|---:|---:|')
        foreach ($group in ($summary | Where-Object { $_.Platform -eq $platform -and $_.Stage -ne 'load' } | Group-Object Fixture, Stage)) {
            $native = $group.Group | Where-Object Renderer -eq native
            $skia = $group.Group | Where-Object Renderer -eq skia-cpu
            $gpu = $group.Group | Where-Object Renderer -eq skia-gpu
            $gpuMetric = if ($gpu) { "$(Format-Number $gpu.FrameMedianMs) / $(Format-Number $gpu.FrameP95Ms)" } else { '—' }
            $dup = $group.Group | Where-Object Renderer -eq 'native-pixel-duplication'
            $browser = $group.Group | Where-Object Renderer -eq webview
            $lines.Add("| $($native.Fixture) | $($native.Stage) | $(Format-Number $native.FrameMedianMs) / $(Format-Number $native.FrameP95Ms) | $(Format-Number $dup.FrameMedianMs) / $(Format-Number $dup.FrameP95Ms) | $(Format-Number $skia.FrameMedianMs) / $(Format-Number $skia.FrameP95Ms) | $gpuMetric | $(Format-Number $browser.FrameMedianMs) / $(Format-Number $browser.FrameP95Ms) |")
        }
        $lines.Add('')
        $lines.Add("Dropped metric callbacks: $(($summary | Where-Object Platform -eq android | Measure-Object DroppedMetricCallbacks -Sum).Sum).")
    }
    $lines.Add('')
    $lines.Add("Validation passed: all $($report.Cases.Count) cases, matching baseline dimensions, valid integer-expanded duplication sizes, pixel limits, and GPU context/paint/downscale/fit/zoom/reset checks. Android requires at least 100 window frame metrics per native/CPU case and a nonempty metric set per asynchronous GPU case.")
    $lines.Add('')
}
$lines.Add('## Interpretation and limits')
$lines.Add('')
$lines.Add('WebView motion runs entirely in JavaScript with requestAnimationFrame and CSS transforms. RAF cadence is not proof of displayed FPS. WebView CPU numbers measure only the embedding app process and exclude isolated browser processes; do not use them to conclude lower total CPU or battery use. Browser dimensions are intrinsic image dimensions, not measured decoded allocations; browser bitmap/GPU memory is unknown. Readiness includes fixture reads, base64 transfer, image loading, and image.decode(). The viewer switches CSS image-rendering between auto and pixelated at 2 physical pixels per source pixel. Fractional-scale filtering may differ from Skia nearest-neighbor.')
$lines.Add('Both native modes transform the same Image control. Pixel duplication prepares an integer-expanded bitmap first; its pixel storage grows quadratically with the replication factor. Interaction cases predecode at target zoom 1 or 4, excluding re-decoding when replication factors change during a gesture. Bitmap MiB is width × height × 4, not peak process/GPU memory. Native filtering remains active after duplication, so this is not pixel-perfect equivalence to nearest-neighbor Skia at every fractional scale. CPU/GPU Skia share bounded decoding and pixel copying. Readiness excludes first presentation and GPU texture upload; GPU callbacks precede presentation. Android window frame metrics do not separately time the GPU rendering thread.')
$lines.Add('')
$lines.Add('The results apply to these implementations, generated fixtures, devices and viewport sizes. Real GIF/video playback, gesture recognition, navigation, original-resolution reloads while zooming, power consumption and memory peaks were not benchmarked. Small-image sampling differs: Skia exercises nearest-neighbor magnification, while native uses its default filter. Process CPU includes benchmark/framework overhead, and the six-load p95 is the maximum observed sample. The CPU control is retained from the previous viewer and shares the new persistent image object with GPU Skia. Historical CPU-only results are preserved in cpu-baseline/.')
$lines | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'Report.md') -Encoding utf8
$summary | Select-Object Platform, Stage, Fixture, Renderer, TargetZoom,
    @{n='Median ms';e={[math]::Round($_.MedianMs,2)}},
    @{n='CPU ms/sample';e={[math]::Round($_.CpuMsPerSample,2)}},
    @{n='Updates/s';e={[math]::Round($_.UpdatesPerSecond,1)}} | Format-Table -AutoSize

