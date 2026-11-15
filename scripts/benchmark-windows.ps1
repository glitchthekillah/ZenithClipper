param([int]$Seconds=15,[int]$Fps=60,[ValidateSet("auto","desktop-duplication")][string]$Backend="auto")
$ErrorActionPreference='Stop'
if($Seconds -lt 5 -or $Seconds -gt 3600) { throw 'Use 5–3600 seconds.' }
$root=Split-Path $PSScriptRoot -Parent
$exe=Join-Path $root 'build/windows/zenithclipper.exe'
$data=Join-Path $root ('.benchmark-home/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $data -Force | Out-Null
$previous=$env:ZENITHCLIPPER_HOME
$env:ZENITHCLIPPER_HOME=$data
$clipDirectory=Join-Path $data 'clips'
@{Fps=$Fps;WindowsCaptureBackend=$Backend;ClipSeconds=3;BufferSeconds=10;MaxBufferMiB=128;ClipDirectory=$clipDirectory;FfmpegPath=(Join-Path (Split-Path $exe -Parent) 'ffmpeg.exe')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $data 'settings.json')
$engine=$null
try {
    $startInfo=[Diagnostics.ProcessStartInfo]::new($exe)
    $startInfo.ArgumentList.Add('daemon')
    $startInfo.UseShellExecute=$false
    $startInfo.CreateNoWindow=$true
    $startInfo.RedirectStandardError=$true
    $startInfo.RedirectStandardOutput=$true
    $engine=[Diagnostics.Process]::Start($startInfo)
    $errorTask=$engine.StandardError.ReadToEndAsync()
    $outputTask=$engine.StandardOutput.ReadToEndAsync()
    $status=$null
    for($attempt=0;$attempt -lt 15;$attempt++){
        Start-Sleep -Seconds 1
        if($engine.HasExited){throw ('Engine exited: '+$errorTask.GetAwaiter().GetResult())}
        $log=Join-Path $data 'engine.log'
        if(!(Test-Path -LiteralPath $log)){continue}
        $status=(& $exe status | ConvertFrom-Json)
        if($status.Ok -and $status.Status.BufferedSeconds -ge 2){break}
    }
    if(!$status.Ok -or !$status.Status.Capturing -or $status.Status.BufferedSeconds -lt 2) { throw ('Capture did not start: '+($status | ConvertTo-Json -Depth 4)+' Log: '+(Get-Content -Raw -LiteralPath (Join-Path $data 'engine.log'))+' Client pipe: '+(& $exe pipe-name)) }
    $childInfo=Get-CimInstance Win32_Process -Filter "ParentProcessId=$($engine.Id)" | Where-Object { $_.Name -eq 'ffmpeg.exe' } | Select-Object -First 1
    if(!$childInfo) { throw 'Encoder child missing.' }
    $encoder=Get-Process -Id $childInfo.ProcessId
    $processes=@($engine,$encoder)
    $before=@{};foreach($process in $processes){$process.Refresh();$before[$process.Id]=$process.TotalProcessorTime.TotalMilliseconds}
    $watch=[Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Seconds $Seconds
    $elapsed=$watch.Elapsed.TotalMilliseconds
    $measurements=foreach($process in $processes){$process.Refresh();[ordered]@{Name=$process.ProcessName;Pid=$process.Id;WorkingSetMiB=[Math]::Round($process.WorkingSet64/1MB,2);PrivateMiB=[Math]::Round($process.PrivateMemorySize64/1MB,2);CpuPercentOfOneLogicalCore=[Math]::Round(($process.TotalProcessorTime.TotalMilliseconds-$before[$process.Id])/$elapsed*100,2)}}
    $final=(& $exe status | ConvertFrom-Json)
    if(!$final.Status.Capturing -or $final.Status.BufferedSeconds -lt 2){throw ('No completed live video during benchmark: '+($final | ConvertTo-Json -Depth 5)+' Log: '+(Get-Content -Raw -LiteralPath (Join-Path $data 'engine.log')))}
    $report=[ordered]@{MeasuredAt=[DateTimeOffset]::Now.ToString('O');Test='Desktop only; not a gaming benchmark';Fps=$Fps;CaptureBackend=$Backend;Seconds=$Seconds;Encoder=$status.Status.Encoder;ExcludedApps=@('Spotify');LogicalProcessors=[Environment]::ProcessorCount;Processes=@($measurements);Status=$final.Status;Notes='Working sets include shared pages; GPU utilization and in-game frame-time impact were not measured.'}
    $reportPath=Join-Path $root 'docs/benchmark-windows.json'
    $report | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $reportPath
    $report | ConvertTo-Json -Depth 7
} finally {
    if($engine -and !$engine.HasExited){ & $exe shutdown | Out-Null; if(!$engine.WaitForExit(5000)){Stop-Process -Id $engine.Id -Force} }
    $env:ZENITHCLIPPER_HOME=$previous
    # Only remove the GUID directory created by this invocation, within this project's benchmark folder.
    $base=[IO.Path]::GetFullPath((Join-Path $root '.benchmark-home'))+[IO.Path]::DirectorySeparatorChar
    $resolved=[IO.Path]::GetFullPath($data)
    if($resolved.StartsWith($base,[StringComparison]::OrdinalIgnoreCase) -and [Guid]::TryParseExact((Split-Path $resolved -Leaf),'N',[ref]([Guid]::Empty))){
        for($attempt=0;$attempt -lt 10;$attempt++){try{Remove-Item -LiteralPath $resolved -Recurse -Force;break}catch{if($attempt -eq 9){throw};Start-Sleep -Milliseconds 200}}
    }
}
