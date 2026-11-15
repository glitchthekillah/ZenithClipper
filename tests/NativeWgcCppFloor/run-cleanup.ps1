$ErrorActionPreference='Stop'
$rootDir=$PSScriptRoot
$env:ZENITH_NATIVE_PACER='legacy'
foreach($variant in @('cleanup-base','cleanup-clear')){
 $folder=Join-Path $rootDir $variant
 $child=Start-Process -FilePath "$folder/NativeVideoComponent.exe" -ArgumentList '--restart-test --cycles 5 --seconds 3' -WorkingDirectory $folder -WindowStyle Hidden -PassThru -RedirectStandardOutput "$folder/run.log" -RedirectStandardError "$folder/run.err"
 $ownedId=$child.Id
 $clock=[Diagnostics.Stopwatch]::StartNew()
 $rows=[Collections.Generic.List[object]]::new()
 $raw=[Collections.Generic.List[object]]::new()
 try{
  while(-not $child.HasExited){
   if($clock.Elapsed.TotalSeconds -gt 60){Stop-Process -Id $ownedId;throw "Owned $variant watchdog expired"}
   $child.Refresh()
   $memory=@{ws=$child.WorkingSet64;peakWs=$child.PeakWorkingSet64;private=$child.PrivateMemorySize64;handles=$child.HandleCount}
   $sample=Get-Counter -Counter '\GPU Process Memory(*)\Dedicated Usage','\GPU Process Memory(*)\Shared Usage','\GPU Process Memory(*)\Local Usage','\GPU Process Memory(*)\Non Local Usage','\GPU Process Memory(*)\Total Committed' -ErrorAction SilentlyContinue
   $matches=@($sample.CounterSamples | Where-Object {$_.InstanceName -like "pid_${ownedId}_*"})
   $completed=@(Select-String -Path "$folder/run.log" -Pattern 'stage=disposed' -ErrorAction SilentlyContinue).Count
   $sums=@{}
   foreach($metric in @('dedicated usage','shared usage','local usage','non local usage','total committed')){$sums[$metric]=[long](($matches|Where-Object {$_.Path.ToLowerInvariant().EndsWith('\'+$metric)}|Measure-Object CookedValue -Sum).Sum)}
   $rows.Add([pscustomobject]@{utc=[DateTime]::UtcNow.ToString('O');elapsed=$clock.Elapsed.TotalSeconds;process=$ownedId;completedCycles=$completed;ws=$memory.ws;peakWs=$memory.peakWs;private=$memory.private;handles=$memory.handles;gpuDedicated=$sums['dedicated usage'];gpuShared=$sums['shared usage'];gpuLocal=$sums['local usage'];gpuNonLocal=$sums['non local usage'];gpuCommitted=$sums['total committed'];instances=(@($matches.InstanceName|Select-Object -Unique)-join '|')})
   foreach($entry in $matches){$raw.Add([pscustomobject]@{elapsed=$clock.Elapsed.TotalSeconds;process=$ownedId;completedCycles=$completed;instance=$entry.InstanceName;path=$entry.Path;bytes=[long]$entry.CookedValue})}
   Start-Sleep -Milliseconds 100
  }
  $child.WaitForExit()
 }finally{
  $rows|Export-Csv -LiteralPath "$folder/memory.csv" -NoTypeInformation
  $raw|Export-Csv -LiteralPath "$folder/gpu-instances.csv" -NoTypeInformation
 }
 Write-Output "$variant exit=$($child.ExitCode) samples=$($rows.Count)"
 Get-Content "$folder/run.log" -Tail 5
 Get-Content "$folder/run.err"
 $maxima=[ordered]@{variant=$variant;exit=$child.ExitCode;samples=$rows.Count}
 foreach($field in @('ws','peakWs','private','handles','gpuDedicated','gpuShared','gpuLocal','gpuNonLocal','gpuCommitted')){$maxima[$field]=($rows|Measure-Object -Property $field -Maximum).Maximum}
 [pscustomobject]$maxima|ConvertTo-Json|Set-Content -LiteralPath "$folder/summary.json" -Encoding utf8
 Get-Content "$folder/summary.json"
 if($child.ExitCode -ne 0){throw "Owned $variant component failed"}
}
