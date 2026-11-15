#requires -Version 7.0
param([string]$EditorDll='', [string]$OutputDirectory='')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$EditorDll){$EditorDll=Join-Path $root 'src/ZenithClipper.Editor.Desktop/bin/Release/net10.0/ZenithClipper.Editor.dll'}
if(!$OutputDirectory){$OutputDirectory=Join-Path $root 'outputs/editor-redesign'}
New-Item -ItemType Directory -Force -Path $OutputDirectory|Out-Null
 $log=Join-Path $OutputDirectory 'verification.log'
$sdk=Join-Path $root 'tools/dotnet/dotnet.exe'
foreach($size in @(@(1000,650),@(1440,920),@(1920,1080))){
    foreach($state in @('empty','populated')){
        $path=Join-Path $OutputDirectory "$($size[0])x$($size[1])-$state.png"
        $info=[Diagnostics.ProcessStartInfo]::new($sdk);$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
        foreach($argument in @($EditorDll,'--headless-smoke','--snapshot',$path,'--width',"$($size[0])",'--height',"$($size[1])")){ $info.ArgumentList.Add($argument) }
        if($state -eq 'empty'){$info.ArgumentList.Add('--empty')}
        $info.Environment['SDL_AUDIODRIVER']='dummy';$info.Environment['SDL_VIDEODRIVER']='dummy'
        $process=[Diagnostics.Process]::Start($info)
        try{$out=$process.StandardOutput.ReadToEndAsync();$err=$process.StandardError.ReadToEndAsync();if(!$process.WaitForExit(45000)){$process.Kill($true);throw 'Headless render watchdog expired.'};$output=$out.GetAwaiter().GetResult();$errorText=$err.GetAwaiter().GetResult();if($process.ExitCode){throw "$output`n$errorText"};Add-Content -LiteralPath $log -Value ("`n=== $path ===`n"+$output+"`n"+$errorText);Write-Host "PASS $path"}finally{$process.Dispose()}
    }
}
# Render-scale verification uses the same logical layout, with 150% software raster DPI.
$path=Join-Path $OutputDirectory '1440x920-populated-150pct.png'
$info=[Diagnostics.ProcessStartInfo]::new($sdk);$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
foreach($argument in @($EditorDll,'--headless-smoke','--snapshot',$path,'--scale','1.5')){$info.ArgumentList.Add($argument)}
$process=[Diagnostics.Process]::Start($info)
try{$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync();if(!$process.WaitForExit(45000)){$process.Kill($true);throw '150% render watchdog expired.'};$output=$stdout.GetAwaiter().GetResult();$errorText=$stderr.GetAwaiter().GetResult();if($process.ExitCode){throw "150% render failed: $output $errorText"};Add-Content -LiteralPath $log -Value ("`n=== $path ===`n"+$output+"`n"+$errorText)}finally{$process.Dispose()}
Write-Host "PASS $path"
