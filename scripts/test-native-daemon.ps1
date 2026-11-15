#requires -Version 7.0
param([string]$Label='native-daemon-integration',[int]$FirstCaptureSeconds=20,[switch]$KeepClips,[switch]$MemoryMap,[double]$MemoryBudgetMiB=100,[string]$EngineDirectory="build/native-recorder-validation",[ValidateRange(1,20)][int]$Cycles=3,[switch]$GpuCounters)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$engineRoot=if([IO.Path]::IsPathRooted($EngineDirectory)){[IO.Path]::GetFullPath($EngineDirectory)}else{[IO.Path]::GetFullPath((Join-Path $root $EngineDirectory))}
$parent=Join-Path $root '.performance-validation'
$testHome=Join-Path $parent ([guid]::NewGuid().ToString('N'))
New-Item $testHome -ItemType Directory -Force|Out-Null
$settings=Get-Content (Join-Path $root 'build/windows/data/settings.json') -Raw|ConvertFrom-Json
$settings.AutoStartCapture=$false;$settings.Fps=0;$settings.OutputWidth=0;$settings.BufferSeconds=8;$settings.ClipSeconds=2;$settings.MaxBufferMiB=128;$settings.ClipDirectory=Join-Path $testHome 'clips';$settings.ClipHotkey='Ctrl+Alt+Shift+F23';$settings.PanelHotkey='Ctrl+Alt+Shift+F24'
$settings|ConvertTo-Json -Depth 8|Set-Content (Join-Path $testHome 'settings.json')
$normalized=[IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($testHome)).ToUpperInvariant()
$hash=[Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($normalized));$pipeName='ZenithClipper-'+[Convert]::ToHexString($hash).Substring(0,20)
$result=[ordered]@{Timestamp=[DateTimeOffset]::Now.ToString("o");NativePacer=$env:ZENITH_NATIVE_PACER;TestedBridgeSha256=(Get-FileHash -LiteralPath (Join-Path $engineRoot "nvenc_shim.dll")).Hash;Cycles=$Cycles;FirstCaptureSeconds=$FirstCaptureSeconds;MemoryBudgetMiB=$MemoryBudgetMiB;TestedBinarySha256=(Get-FileHash -LiteralPath (Join-Path $engineRoot "ZenithClipper.Recorder.exe")).Hash;Label=$Label;IncludesIpcAndHotkeys=$true;Pass=$false;Samples=[Collections.Generic.List[object]]::new();Clips=@();Responses=@()};$p=$null;if($MemoryMap){Add-Type -Path (Join-Path $root "scripts/CaptureMemoryMap.cs");$result.MemoryMaps=@()}
$gpuNext=0
if($GpuCounters){$result.GpuSamples=[Collections.Generic.List[object]]::new()}
function Read-GpuSample {
 try{
  $memory=@(Get-CimInstance -ClassName Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory -Filter ("Name LIKE 'pid_"+$p.Id+"_%'") -ErrorAction Stop|Select-Object Name,DedicatedUsage,SharedUsage,LocalUsage,NonLocalUsage,TotalCommitted)
  $engines=@(Get-CimInstance -ClassName Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine -Filter ("Name LIKE 'pid_"+$p.Id+"_%'") -ErrorAction Stop|Select-Object Name,UtilizationPercentage)
  $result.GpuSamples.Add(@{Seconds=$clock.Elapsed.TotalSeconds;Memory=$memory;Engines=$engines})
 }catch{$result.GpuCounterError=$_.Exception.Message}
}
function Sample {
 if($GpuCounters -and $clock.Elapsed.TotalSeconds -ge $script:gpuNext){$script:gpuNext=$clock.Elapsed.TotalSeconds+2;Read-GpuSample}
 $p.Refresh();$result.Samples.Add(@{Seconds=$clock.Elapsed.TotalSeconds;ResidentMiB=$p.WorkingSet64/1MB;PeakResidentMiB=$p.PeakWorkingSet64/1MB;PrivateCommitMiB=$p.PrivateMemorySize64/1MB;CpuSeconds=$p.TotalProcessorTime.TotalSeconds})
}
function Request($command,$newSettings=$null) {
 $requestClock=[Diagnostics.Stopwatch]::StartNew()
 $pipe=[IO.Pipes.NamedPipeClientStream]::new('.',$pipeName,[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous)
 try {$pipe.Connect(3000);$reader=[IO.StreamReader]::new($pipe,[Text.Encoding]::UTF8,$false,4096,$true);$writer=[IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false),4096,$true);$writer.AutoFlush=$true
 $request=@{Command=$command};if($command -eq 'save'){$request.Seconds=2};if($newSettings){$request.Settings=$newSettings}
 $writer.WriteLine(($request|ConvertTo-Json -Depth 10 -Compress));$pending=$reader.ReadLineAsync()
 while(!$pending.IsCompleted){Sample;if($clock.Elapsed.TotalSeconds -gt ($FirstCaptureSeconds+$Cycles*12+45)){throw 'Integration watchdog elapsed'};Start-Sleep -Milliseconds 80}
 $reply=$pending.GetAwaiter().GetResult()|ConvertFrom-Json;$reader.Dispose();$writer.Dispose();$result.Responses+=@{Command=$command;Reply=$reply;ElapsedMilliseconds=$requestClock.Elapsed.TotalMilliseconds};if(!$reply.Ok){throw "$command failed: $($reply.Error)"};return $reply
 }finally{$pipe.Dispose()}
}
function Observe([int]$seconds){$until=$clock.Elapsed.TotalSeconds+$seconds;while($clock.Elapsed.TotalSeconds -lt $until){if($p.HasExited){throw 'Daemon exited unexpectedly'};Sample;Start-Sleep -Milliseconds 80}}
try {
 $info=[Diagnostics.ProcessStartInfo]::new((Join-Path $engineRoot 'ZenithClipper.Recorder.exe'));$info.ArgumentList.Add('daemon');$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.Environment['ZENITHCLIPPER_HOME']=$testHome;$null=$info.Environment.Remove('DOTNET_PROCESSOR_COUNT')
 $p=[Diagnostics.Process]::Start($info);$p.PriorityClass=[Diagnostics.ProcessPriorityClass]::BelowNormal;$out=$p.StandardOutput.ReadToEndAsync();$err=$p.StandardError.ReadToEndAsync();$clock=[Diagnostics.Stopwatch]::StartNew()
 Observe 1;$null=Request 'status';$null=Request 'settings'
 for($cycle=0;$cycle -lt $Cycles;$cycle++) {
 $null=Request 'resume';Observe $(if($cycle -eq 0){$FirstCaptureSeconds}else{4})
 $status=Request 'status';if(!$status.Status.Capturing){throw 'Capture did not become active'}
 if($MemoryMap){$result.MemoryMaps+=@{Cycle=$cycle;Phase="capturing";Map=[CaptureMemoryMap]::Read($p.Id)}}
 $saved=Request 'save';$result.Clips+= $saved.Path
 $settings.ClipHotkey=$(if($cycle%2){'Ctrl+Alt+Shift+F23'}else{'Ctrl+Alt+Shift+F22'});$null=Request 'configure' $settings
 $null=Request 'pause';Observe 1
 if($MemoryMap){$result.MemoryMaps+=@{Cycle=$cycle;Phase="paused";Map=[CaptureMemoryMap]::Read($p.Id)}}
 }
 $null=Request 'shutdown';if(!$p.WaitForExit(8000)){throw 'Daemon shutdown watchdog elapsed'};$result.Exit=$p.ExitCode;$result.Stderr=$err.GetAwaiter().GetResult();if($p.ExitCode){throw 'Daemon exit failure'}
 $result.EngineLog=Get-Content (Join-Path $testHome 'engine.log') -Raw
 if($result.EngineLog -match 'hotkey|failed|error'){throw 'Daemon log reported a registration or runtime failure'}
 foreach($clip in $result.Clips){$probe=& (Join-Path $root 'build/windows/ffprobe.exe') -v error -show_streams -of json $clip;if($LASTEXITCODE){throw 'Saved clip probe failed'};$metadata=($probe -join "`n")|ConvertFrom-Json;$video=$metadata.streams|Where-Object codec_type -eq video;if($video.width -ne 2560 -or $video.height -ne 1600 -or $video.r_frame_rate -ne '165/1'){throw 'Native format changed'}
 & (Join-Path $root 'build/windows/ffmpeg.exe') -v error -xerror -threads 1 -filter_threads 1 -i $clip -f null - 2>&1|Out-String|ForEach-Object {if($_.Trim()){throw $_}};if($LASTEXITCODE){throw 'Strict decode failed'}}
 if($KeepClips){$evidence=Join-Path $root ("outputs/"+$Label+"-clips");New-Item $evidence -ItemType Directory -Force|Out-Null;foreach($clip in $result.Clips){Copy-Item -LiteralPath $clip -Destination $evidence -Force};$result.ClipEvidence=$evidence}
 $result.FunctionalPass=$true
 $result.PeakResidentMiB=($result.Samples.PeakResidentMiB|Measure-Object -Maximum).Maximum
 $result.MemoryBudgetPass=$result.PeakResidentMiB -le $MemoryBudgetMiB
 if($result.PeakResidentMiB -gt $MemoryBudgetMiB){throw "Resident memory exceeded $MemoryBudgetMiB MiB"}
 $result.Pass=$true
}catch{$result.Error=$_.Exception.ToString()}
finally{
 if($p){if(!$p.HasExited){$p.Kill($true);$p.WaitForExit()};$p.Dispose()}
 [IO.File]::WriteAllText((Join-Path $root ('outputs/'+$Label+'.json')),($result|ConvertTo-Json -Depth 18))
 $checked=[IO.Path]::GetFullPath($testHome);$prefix=[IO.Path]::GetFullPath($parent)+[IO.Path]::DirectorySeparatorChar;if(!$checked.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe cleanup'};Remove-Item -LiteralPath $checked -Recurse -Force
}
[pscustomobject]@{Pass=$result.Pass;PeakResidentMiB=$result.PeakResidentMiB;Error=$result.Error;Exit=$result.Exit;Clips=$result.Clips.Count;EngineLog=$result.EngineLog}|ConvertTo-Json
if(!$result.Pass){exit 1}









