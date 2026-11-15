#requires -Version 7.0
param([string]$Label='native-full-first')
$ErrorActionPreference='Stop';if($Label -notmatch '^[a-zA-Z0-9_-]+$'){throw 'Invalid label'}
$root=Split-Path $PSScriptRoot -Parent;$parent=Join-Path $root '.performance-validation';$testHome=Join-Path $parent ([guid]::NewGuid().ToString('N'));New-Item $testHome -ItemType Directory -Force|Out-Null
$settings=Get-Content (Join-Path $root 'build/windows/data/settings.json') -Raw|ConvertFrom-Json;$settings.Fps=0;$settings.OutputWidth=0;$settings.AutoStartCapture=$false;$settings.BufferSeconds=8;$settings.ClipSeconds=2;$settings.MaxBufferMiB=128;$settings.ClipDirectory=Join-Path $testHome 'clips';$settings.ClipHotkey='Ctrl+Alt+Shift+F23';$settings.PanelHotkey='Ctrl+Alt+Shift+F24';$settings|ConvertTo-Json -Depth 8|Set-Content (Join-Path $testHome 'settings.json')
$result=[ordered]@{Label=$Label;Timestamp=[DateTimeOffset]::Now.ToString('o');Samples=@();IncludesIpcAndHotkeys=$false;Pass=$false};$p=$null
try
{
 $info=[Diagnostics.ProcessStartInfo]::new((Join-Path $root 'build/native-recorder-validation/ZenithClipper.Recorder.exe'));$info.ArgumentList.Add('--self-test');$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.Environment['ZENITHCLIPPER_HOME']=$testHome;$null=$info.Environment.Remove('DOTNET_PROCESSOR_COUNT')
 $p=[Diagnostics.Process]::Start($info);$p.PriorityClass=[Diagnostics.ProcessPriorityClass]::BelowNormal;$stdout=$p.StandardOutput.ReadToEndAsync();$stderr=$p.StandardError.ReadToEndAsync();$clock=[Diagnostics.Stopwatch]::StartNew()
 while(!$p.WaitForExit(80)){$p.Refresh();$result.Samples+=@{Seconds=$clock.Elapsed.TotalSeconds;ResidentMiB=$p.WorkingSet64/1MB;PeakResidentMiB=$p.PeakWorkingSet64/1MB;PrivateCommitMiB=$p.PrivateMemorySize64/1MB;CpuSeconds=$p.TotalProcessorTime.TotalSeconds};if($clock.Elapsed.TotalSeconds -gt 45){$p.Kill($true);$p.WaitForExit();throw 'Native full pipeline watchdog elapsed'}}
 $result.Exit=$p.ExitCode;$result.Stdout=$stdout.GetAwaiter().GetResult();$result.Stderr=$stderr.GetAwaiter().GetResult();if($result.Exit){throw 'Native self-test failed'}
 $clips=@(Get-ChildItem (Join-Path $testHome 'clips') -Filter *.mp4);if($clips.Count -ne 1){throw 'Expected one saved clip'}
 $meta=& (Join-Path $root 'build/windows/ffprobe.exe') -v error -show_streams -show_format -of json $clips[0].FullName;if($LASTEXITCODE){throw 'Probe failed'};$result.Metadata=($meta -join "`n")|ConvertFrom-Json
 $v=$result.Metadata.streams|Where-Object codec_type -eq video;$a=$result.Metadata.streams|Where-Object codec_type -eq audio;if($v.width -ne 2560 -or $v.height -ne 1600 -or !$a){throw 'Native dimensions/audio missing'}
 $decode=[Diagnostics.ProcessStartInfo]::new((Join-Path $root 'build/windows/ffmpeg.exe'));$decode.UseShellExecute=$false;$decode.CreateNoWindow=$true;$decode.RedirectStandardError=$true;$decode.RedirectStandardOutput=$true;foreach($arg in @('-v','error','-xerror','-threads','1','-filter_threads','1','-i',$clips[0].FullName,'-f','null','-')){$decode.ArgumentList.Add($arg)};$d=[Diagnostics.Process]::Start($decode);$d.PriorityClass=[Diagnostics.ProcessPriorityClass]::BelowNormal;$de=$d.StandardError.ReadToEndAsync();$do=$d.StandardOutput.ReadToEndAsync();if(!$d.WaitForExit(12000)){$d.Kill($true);$d.WaitForExit();throw 'Full strict decode watchdog elapsed'};$result.DecodeExit=$d.ExitCode;$result.DecodeErrors=$de.GetAwaiter().GetResult();$d.Dispose();if($result.DecodeExit){throw 'Strict full decode failed'};$result.Pass=$true
}
catch{$result.Error=$_.Exception.ToString()}
finally
{
 if($p){if(!$p.HasExited){$p.Kill($true);$p.WaitForExit()};$p.Dispose()}
 [IO.File]::WriteAllText((Join-Path $root ('outputs/'+$Label+'.json')),($result|ConvertTo-Json -Depth 14))
 $checked=[IO.Path]::GetFullPath($testHome);$expected=[IO.Path]::GetFullPath($parent)+[IO.Path]::DirectorySeparatorChar;if(!$checked.StartsWith($expected,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe cleanup'};Remove-Item -LiteralPath $checked -Recurse -Force
}
[pscustomobject]@{Pass=$result.Pass;Error=$result.Error;Exit=$result.Exit;PeakResidentMiB=($result.Samples.PeakResidentMiB|Measure-Object -Maximum).Maximum;PeakPrivateMiB=($result.Samples.PrivateCommitMiB|Measure-Object -Maximum).Maximum;Stdout=$result.Stdout;Stderr=$result.Stderr}|ConvertTo-Json -Depth 3
if(!$result.Pass){exit 1}
