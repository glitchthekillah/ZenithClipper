#requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$testRoot=Join-Path $root ('.performance-validation/'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force|Out-Null
$variants=@(@{Name='low-delay-async1-first';Input=@();Output=@('-flags','+low_delay')},@{Name='low-delay-async1-second';Input=@();Output=@('-flags','+low_delay')})
$results=@()
try {
 foreach($variant in $variants){
  $path=Join-Path $testRoot ($variant.Name+'.mp4')
  $info=[Diagnostics.ProcessStartInfo]::new((Join-Path $root 'build/windows/ffmpeg.exe'));$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardError=$true;$info.RedirectStandardOutput=$true
  $arguments=@('-hide_banner','-loglevel','info','-filter_threads','1','-filter_complex_threads','1','-y')+$variant.Input+@('-f','lavfi','-i','vsrc_amf=monitor_index=0:framerate=165','-map','0:v:0','-vf','settb=1/165,setpts=N','-c:v','h264_amf','-quality','speed','-async_depth','1','-vbaq','0','-rc','cqp','-qp_i','20','-qp_p','20')+$variant.Output+@('-r','165','-fps_mode','cfr','-g','330','-bf','0','-an','-frames:v','495',$path)
  foreach($arg in $arguments){$info.ArgumentList.Add($arg)}
  $process=[Diagnostics.Process]::Start($info);$process.PriorityClass=[Diagnostics.ProcessPriorityClass]::BelowNormal;$errors=$process.StandardError.ReadToEndAsync();$stdout=$process.StandardOutput.ReadToEndAsync();$timer=[Diagnostics.Stopwatch]::StartNew();$samples=@();$timedOut=$false
  try{while(!$process.WaitForExit(120)){if($timer.Elapsed.TotalSeconds -gt 10){$timedOut=$true;$process.Kill($true);break};$process.Refresh();$samples+=@{Seconds=[math]::Round($timer.Elapsed.TotalSeconds,3);WorkingSetMiB=[math]::Round($process.WorkingSet64/1MB,2);PrivateMiB=[math]::Round($process.PrivateMemorySize64/1MB,2);CpuSeconds=$process.TotalProcessorTime.TotalSeconds}};$process.WaitForExit();$result=[ordered]@{Name=$variant.Name;Exit=$process.ExitCode;TimedOut=$timedOut;ElapsedSeconds=$timer.Elapsed.TotalSeconds;Samples=$samples;Log=$errors.GetAwaiter().GetResult()};$results+=$result}finally{if(!$process.HasExited){$process.Kill($true)};$process.Dispose()}
  if($result.Exit -eq 0){
   $probe=& (Join-Path $root 'build/windows/ffprobe.exe') -v error -show_streams -of json $path|ConvertFrom-Json;$result.Metadata=$probe
   $decode=[Diagnostics.ProcessStartInfo]::new((Join-Path $root 'build/windows/ffmpeg.exe'));$decode.UseShellExecute=$false;$decode.CreateNoWindow=$true;$decode.RedirectStandardError=$true;foreach($arg in @('-v','error','-xerror','-threads','1','-filter_threads','1','-i',$path,'-f','null','-')){$decode.ArgumentList.Add($arg)};$p=[Diagnostics.Process]::Start($decode);$p.PriorityClass=[Diagnostics.ProcessPriorityClass]::BelowNormal;$err=$p.StandardError.ReadToEndAsync();if(!$p.WaitForExit(8000)){$p.Kill($true);$p.WaitForExit();$result.DecodeTimeout=$true};$result.DecodeExit=$p.ExitCode;$result.DecodeErrors=$err.GetAwaiter().GetResult();$p.Dispose()
  }
  [IO.File]::WriteAllText((Join-Path $root 'outputs/memory-frame-queue-candidates.json'),($results|ConvertTo-Json -Depth 12))
  [pscustomobject]@{Name=$result.Name;Exit=$result.Exit;Seconds=[math]::Round($result.ElapsedSeconds,2);PeakMiB=($samples.WorkingSetMiB|Measure-Object -Maximum).Maximum;SteadyMiB=($samples|Where-Object Seconds -gt 1|Measure-Object WorkingSetMiB -Average).Average;DecodeExit=$result.DecodeExit}|ConvertTo-Json
 }
}finally{$checked=[IO.Path]::GetFullPath($testRoot);$expected=[IO.Path]::GetFullPath((Join-Path $root '.performance-validation'))+[IO.Path]::DirectorySeparatorChar;if(!$checked.StartsWith($expected,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe cleanup target'};Remove-Item -LiteralPath $checked -Recurse -Force}
