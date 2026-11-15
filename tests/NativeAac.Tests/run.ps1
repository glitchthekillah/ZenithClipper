$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot)
& (Join-Path $repo 'src/ZenithClipper.Native.Audio/AacBuild.ps1')
if($LASTEXITCODE){throw 'Native AAC build failed'}
& (Join-Path $PSScriptRoot 'publish-local.ps1')
if($LASTEXITCODE){throw 'AAC AOT publish failed'}
Copy-Item -LiteralPath (Join-Path $repo 'src/ZenithClipper.Native.Audio/AacBuild/zenith_aac.dll') -Destination (Join-Path $PSScriptRoot 'publish/zenith_aac.dll') -Force
$probe=Start-Process -FilePath (Join-Path $PSScriptRoot 'publish/NativeAac.Tests.exe') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $PSScriptRoot 'result.log') -RedirectStandardError (Join-Path $PSScriptRoot 'stderr.log')
if(-not $probe.WaitForExit(30000)){Stop-Process -Id $probe.Id -Force;throw 'AAC probe watchdog expired'}
if($probe.ExitCode){throw "AAC probe exited $($probe.ExitCode)"}
$ff=Join-Path $repo 'tools/ffmpeg-package/ffmpeg-9.0.2-essentials_build/bin'
& (Join-Path $ff 'ffprobe.exe') -v error -show_streams -show_packets -of json (Join-Path $PSScriptRoot 'publish/synthetic.aac') > (Join-Path $PSScriptRoot 'probe.json')
if($LASTEXITCODE){throw 'AAC probe failed'}
& (Join-Path $ff 'ffmpeg.exe') -v error -xerror -err_detect explode -i (Join-Path $PSScriptRoot 'publish/synthetic.aac') -map 0:a:0 -f s16le -acodec pcm_s16le -y (Join-Path $PSScriptRoot 'publish/decoded.pcm') 2> (Join-Path $PSScriptRoot 'decode.log')
if($LASTEXITCODE){throw 'Strict AAC decode failed'}
Push-Location $repo
try{& tools/native-toolchain/python/python.exe tests/NativeAac.Tests/verify.py > tests/NativeAac.Tests/verification.log;if($LASTEXITCODE){throw 'AAC verification failed'}}finally{Pop-Location}
Get-Content (Join-Path $PSScriptRoot 'result.log')
Get-Content (Join-Path $PSScriptRoot 'verification.log')
