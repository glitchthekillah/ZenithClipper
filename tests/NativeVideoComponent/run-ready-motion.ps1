param([int]$OutputWidth = 0, [ValidateRange(1,2)][int]$Buffers = 2, [ValidatePattern('^[a-zA-Z0-9-]+$')][string]$Label = 'native-ready-motion')
$ErrorActionPreference = 'Stop'
$publish = Join-Path $PSScriptRoot 'publish'
$metric = Join-Path $publish 'motion-window-metrics.txt'
$log = Join-Path $PSScriptRoot ($Label + '.log')
$errorLog = Join-Path $PSScriptRoot ($Label + '.stderr.txt')
$clock = [Diagnostics.Stopwatch]::StartNew()
$started = [DateTime]::UtcNow
$pattern = $null
$probe = $null
$previousTrace = $env:ZENITH_NATIVE_VIDEO_TRACE
try {
    $env:ZENITH_NATIVE_VIDEO_TRACE = '1'
    $pattern = Start-Process -FilePath (Join-Path $publish 'motion-window.exe') -WindowStyle Hidden -PassThru
    $ready = $false
    while ($clock.ElapsedMilliseconds -lt 2000 -and !$pattern.HasExited) {
        if ((Test-Path -LiteralPath $metric) -and (Get-Item -LiteralPath $metric).LastWriteTimeUtc -ge $started) {
            $line = Get-Content -LiteralPath $metric -Raw
            if ($line -match 'frames=(\d+)' -and [int]$Matches[1] -ge 100) { $ready = $true; break }
        }
        Start-Sleep -Milliseconds 20
        $pattern.Refresh()
    }
    if (!$ready) { throw 'Owned stimulus did not render 100 frames before capture; no capture benchmark was launched.' }
    $arguments = @('--buffers', [string]$Buffers)
    if ($OutputWidth -gt 0) { $arguments += @('--width', [string]$OutputWidth) }
    $probe = Start-Process -FilePath (Join-Path $publish 'NativeVideoComponent.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $log -RedirectStandardError $errorLog
    if (!$probe.WaitForExit([Math]::Max(1,10000-[int]$clock.ElapsedMilliseconds))) { Stop-Process -Id $probe.Id -Force; throw 'Ten-second capture watchdog expired.' }
    if ($probe.ExitCode -ne 0) { throw ('Component test failed with exit ' + $probe.ExitCode) }
    Copy-Item -LiteralPath (Join-Path $publish 'component.h264') -Destination (Join-Path $PSScriptRoot ($Label + '.h264'))
}
finally {
    if ($probe -and !$probe.HasExited) { Stop-Process -Id $probe.Id -Force }
    if ($pattern -and !$pattern.HasExited -and !$pattern.WaitForExit([Math]::Max(1,10000-[int]$clock.ElapsedMilliseconds))) { Stop-Process -Id $pattern.Id -Force }
    if ((Test-Path -LiteralPath $metric) -and (Get-Item -LiteralPath $metric).LastWriteTimeUtc -ge $started) {
        Copy-Item -LiteralPath $metric -Destination (Join-Path $PSScriptRoot ($Label + '-stimulus.txt'))
        Get-Content -LiteralPath $metric
    }
    $env:ZENITH_NATIVE_VIDEO_TRACE = $previousTrace
}
Get-Content -LiteralPath $log
Get-Content -LiteralPath $errorLog
