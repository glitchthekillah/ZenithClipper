$ErrorActionPreference='Stop'
$included=$null;$excluded=$null;$capture=$null
try {
    $common=@('-hide_banner','-loglevel','error','-nodisp','-autoexit','-volume','100','-af','volume=0.02','-f','lavfi','-i')
    $included=Start-Process -FilePath (Join-Path $PSScriptRoot 'ZenithToneInclude.exe') -ArgumentList ($common+@('sine=frequency=440:sample_rate=48000:duration=35')) -Environment @{SDL_AUDIODRIVER='wasapi'} -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $PSScriptRoot 'included-player.stdout.txt') -RedirectStandardError (Join-Path $PSScriptRoot 'included-player.stderr.txt')
    $excluded=Start-Process -FilePath (Join-Path $PSScriptRoot 'ZenithToneExclude.exe') -ArgumentList ($common+@('sine=frequency=880:sample_rate=48000:duration=35')) -Environment @{SDL_AUDIODRIVER='wasapi'} -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $PSScriptRoot 'excluded-player.stdout.txt') -RedirectStandardError (Join-Path $PSScriptRoot 'excluded-player.stderr.txt')
    $capture=Start-Process -FilePath (Join-Path $PSScriptRoot 'publish/NativeAudioExclusion.exe') -ArgumentList @([string]$included.Id,[string]$excluded.Id) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $PSScriptRoot 'capture.log') -RedirectStandardError (Join-Path $PSScriptRoot 'capture.stderr.txt')
    if(!$capture.WaitForExit(30000)){Stop-Process -Id $capture.Id -Force;throw 'Owned audio test watchdog expired.'}
    Write-Output ('Capture exit: '+$capture.ExitCode)
    Get-Content -LiteralPath (Join-Path $PSScriptRoot 'capture.log')
    Get-Content -LiteralPath (Join-Path $PSScriptRoot 'capture.stderr.txt')
    if($capture.ExitCode -ne 0){throw 'Native audio exclusion validation failed.'}
}
finally {
    foreach($owned in @($capture,$included,$excluded)){
        if($owned -and !$owned.HasExited){Stop-Process -Id $owned.Id -Force;$owned.WaitForExit(1000)|Out-Null}
    }
    Get-Content -LiteralPath (Join-Path $PSScriptRoot 'included-player.stderr.txt') -ErrorAction SilentlyContinue
    Get-Content -LiteralPath (Join-Path $PSScriptRoot 'excluded-player.stderr.txt') -ErrorAction SilentlyContinue
}

