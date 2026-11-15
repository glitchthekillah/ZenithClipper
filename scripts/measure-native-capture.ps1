#requires -Version 7.0
param([string]$Label="full-native-memory-validation",[switch]$MemoryMap,[string]$EnginePath="build/windows/zenithclipper.exe",[ValidateRange(0,240)][int]$FrameRate=15,[ValidateRange(1,3)][int]$SampleSeconds=1,[switch]$NoCompatibilityFallback)
$ErrorActionPreference='Stop'
if($Label -notmatch '^[a-zA-Z0-9_-]+$'){throw 'Invalid diagnostic label.'}
$root=Split-Path $PSScriptRoot -Parent
$parent=Join-Path $root '.performance-validation'
New-Item -ItemType Directory -Path $parent -Force|Out-Null
$profileHome=Join-Path $parent ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $profileHome -Force|Out-Null
$settings=Get-Content (Join-Path $root 'build/windows/data/settings.json') -Raw|ConvertFrom-Json
$settings.FfmpegPath=Join-Path $root 'build/windows/ffmpeg.exe'
$settings.AutoStartCapture=$false;$settings.WindowsCaptureBackend='auto';$settings.Encoder='auto';$settings.Fps=$FrameRate
$settings.ClipSeconds=2;$settings.BufferSeconds=8;$settings.MaxBufferMiB=256;$settings.ClipDirectory=Join-Path $profileHome 'clips'
$settings.ClipHotkey='Ctrl+Alt+Shift+F23';$settings.PanelHotkey='Ctrl+Alt+Shift+F24'
[IO.File]::WriteAllText((Join-Path $profileHome 'settings.json'),($settings|ConvertTo-Json -Depth 20))
$key=[IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($profileHome)).ToUpperInvariant()
$pipeName='ZenithClipper-'+[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($key))).Substring(0,20)
function Send-ProfileRequest([string]$Command,[int]$Timeout=12000){
 $pipe=[IO.Pipes.NamedPipeClientStream]::new('.',$pipeName,[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous)
 try{$pipe.Connect(1000);$writer=[IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false),4096,$true);$writer.AutoFlush=$true;$reader=[IO.StreamReader]::new($pipe,[Text.UTF8Encoding]::new($false),$false,4096,$true);$writer.WriteLine((@{Command=$Command}|ConvertTo-Json -Compress));$reply=$reader.ReadLineAsync().WaitAsync([TimeSpan]::FromMilliseconds($Timeout)).GetAwaiter().GetResult()|ConvertFrom-Json;if(!$reply.Ok){throw $reply.Error};return $reply}finally{$pipe.Dispose()}
}
$info=[Diagnostics.ProcessStartInfo]::new((Join-Path $root $EnginePath))
$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.Environment['ZENITHCLIPPER_HOME']=$profileHome;$null=$info.Environment.Remove('DOTNET_PROCESSOR_COUNT');$info.ArgumentList.Add('daemon');if($NoCompatibilityFallback){$info.ArgumentList.Add('--no-compatibility-fallback')}
$process=[Diagnostics.Process]::Start($info);$process.PriorityClass=[Diagnostics.ProcessPriorityClass]::BelowNormal
$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync();$result=[ordered]@{Test='isolated full native capture/audio/save';Timestamp=[DateTimeOffset]::Now.ToString('o');RequestedFrameRate=$FrameRate;SampleSeconds=$SampleSeconds;LogicalProcessors=[int]$env:NUMBER_OF_PROCESSORS;Samples=@()}
try{
 $ready=$false;for($i=0;$i -lt 20;$i++){try{$null=Send-ProfileRequest status 1000;$ready=$true;break}catch{if($process.HasExited){throw 'Profile daemon exited before readiness.'};Start-Sleep -Milliseconds 100}}
 if(!$ready){throw 'Profile daemon did not become ready.'}
 $start=[Diagnostics.Stopwatch]::StartNew();$null=Send-ProfileRequest resume 12000;$result.StartupSeconds=[math]::Round($start.Elapsed.TotalSeconds,3)
 $status=(Send-ProfileRequest status).Status;$result.Status=$status
 if(!$status.Capturing -or $status.Encoder -ne 'amf'){throw 'Adapter-aware auto did not enter AMF capture.'}
 $child=Get-CimInstance Win32_Process -Filter "ParentProcessId=$($process.Id)"|Where-Object Name -eq 'ffmpeg.exe'|Select-Object -First 1
 if(!$child){throw 'Capture child missing.'}
 $capture=Get-Process -Id $child.ProcessId;$watch=[Diagnostics.Stopwatch]::StartNew()
 while($watch.Elapsed.TotalSeconds -lt $SampleSeconds){$process.Refresh();$capture.Refresh();$result.Samples+=@{Seconds=[math]::Round($watch.Elapsed.TotalSeconds,3);EngineWorkingSetMiB=[math]::Round($process.WorkingSet64/1MB,2);EncoderWorkingSetMiB=[math]::Round($capture.WorkingSet64/1MB,2);EnginePrivateMiB=[math]::Round($process.PrivateMemorySize64/1MB,2);EncoderPrivateMiB=[math]::Round($capture.PrivateMemorySize64/1MB,2);CpuSeconds=$process.TotalProcessorTime.TotalSeconds+$capture.TotalProcessorTime.TotalSeconds};Start-Sleep -Milliseconds 150}
 if($MemoryMap){Add-Type -Path (Join-Path $root 'scripts/CaptureMemoryMap.cs');$result.EngineMemoryMap=[CaptureMemoryMap]::Read($process.Id);$result.EncoderMemoryMap=[CaptureMemoryMap]::Read($capture.Id)}
 $saved=Send-ProfileRequest save 10000;$result.SavedClip=$saved.Path
 $null=Send-ProfileRequest pause 8000
 $probeInfo=[Diagnostics.ProcessStartInfo]::new((Join-Path $root 'build/windows/ffprobe.exe'));$probeInfo.UseShellExecute=$false;$probeInfo.CreateNoWindow=$true;$probeInfo.RedirectStandardOutput=$true;$probeInfo.RedirectStandardError=$true;foreach($arg in @('-v','error','-show_streams','-show_format','-of','json',$saved.Path)){$probeInfo.ArgumentList.Add($arg)}
 $probe=[Diagnostics.Process]::Start($probeInfo);$probeOut=$probe.StandardOutput.ReadToEndAsync();$probeErr=$probe.StandardError.ReadToEndAsync();if(!$probe.WaitForExit(5000)){$probe.Kill($true);throw 'Probe watchdog elapsed.'};if($probe.ExitCode){throw $probeErr.GetAwaiter().GetResult()};$result.ClipMetadata=$probeOut.GetAwaiter().GetResult()|ConvertFrom-Json;$probe.Dispose()
 $video=$result.ClipMetadata.streams|Where-Object codec_type -eq video|Select-Object -First 1
 if($video.width -ne 2560 -or $video.height -ne 1600){throw 'Native output resolution changed.'}
 if(!($result.ClipMetadata.streams|Where-Object codec_type -eq audio)){throw 'Audio track missing.'}
 $parts=$video.avg_frame_rate.Split('/');$rate=[double]$parts[0]/[double]$parts[1]
 $expectedFps=if($FrameRate -gt 0){$FrameRate}else{165}; if([math]::Abs($rate-$expectedFps)>1){throw "Output frame rate changed: $rate"}
 $decodeInfo=[Diagnostics.ProcessStartInfo]::new((Join-Path $root 'build/windows/ffmpeg.exe'));$decodeInfo.UseShellExecute=$false;$decodeInfo.CreateNoWindow=$true;$decodeInfo.RedirectStandardError=$true;$decodeInfo.RedirectStandardOutput=$true
 foreach($arg in @('-v','error','-xerror','-threads','1','-filter_threads','1','-i',$saved.Path,'-map','0:v:0','-map','0:a:0','-frames:v','3','-frames:a','4','-f','null','-')){$decodeInfo.ArgumentList.Add($arg)}
 $decoder=[Diagnostics.Process]::Start($decodeInfo);$decoder.PriorityClass=[Diagnostics.ProcessPriorityClass]::BelowNormal;$decodeErr=$decoder.StandardError.ReadToEndAsync();$decodeOut=$decoder.StandardOutput.ReadToEndAsync();if(!$decoder.WaitForExit(5000)){$decoder.Kill($true);$decoder.WaitForExit();throw 'Initial-frame decode watchdog elapsed.'};$result.InitialDecodeExit=$decoder.ExitCode;$result.InitialDecodeErrors=$decodeErr.GetAwaiter().GetResult();$decoder.Dispose();if($result.InitialDecodeExit){throw ('Initial frame/audio decode failed: '+$result.InitialDecodeErrors)}
 $result.Pass=$true
}catch{$result.Pass=$false;$result.Error=$_.Exception.Message;Write-Output ('FAIL '+$_.Exception.Message)}finally{
 try{$null=Send-ProfileRequest shutdown 2000}catch{}
 if(!$process.WaitForExit(4000)){$process.Kill($true);$process.WaitForExit()}
 $result.EngineExit=$process.ExitCode;$result.EngineLog=$stderr.GetAwaiter().GetResult();$process.Dispose()
 [IO.File]::WriteAllText((Join-Path $root ('outputs/'+$Label+'.json')),($result|ConvertTo-Json -Depth 16))
 $resolved=[IO.Path]::GetFullPath($profileHome);$expected=[IO.Path]::GetFullPath($parent)+[IO.Path]::DirectorySeparatorChar
 if(!$resolved.StartsWith($expected,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe profile cleanup path.'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
[pscustomobject]$result|Select-Object Pass,Error,StartupSeconds,Status,@{n='PeakCombinedWorkingSetMiB';e={($_.Samples|ForEach-Object {$_.EngineWorkingSetMiB+$_.EncoderWorkingSetMiB}|Measure-Object -Maximum).Maximum}},@{n='PeakCombinedPrivateMiB';e={($_.Samples|ForEach-Object {$_.EnginePrivateMiB+$_.EncoderPrivateMiB}|Measure-Object -Maximum).Maximum}}|ConvertTo-Json -Depth 4
if(!$result.Pass){exit 1}
