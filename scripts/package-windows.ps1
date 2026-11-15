param([string]$FfmpegDirectory = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
& "$PSScriptRoot/dev.ps1" app
if ($LASTEXITCODE) { exit $LASTEXITCODE }
& "$PSScriptRoot/dev.ps1" panel
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$destination = Join-Path $root 'build/windows'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$checked=[IO.Path]::GetFullPath($destination)
$expected=[IO.Path]::GetFullPath((Join-Path $root 'build/windows'))
if($checked -ne $expected){throw 'Unexpected package directory.'}
foreach($old in Get-ChildItem -LiteralPath $checked -Force){
    if($old.Name -eq 'data'){continue}
    if(!$old.FullName.StartsWith($checked+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe package cleanup path.'}
    Remove-Item -LiteralPath $old.FullName -Recurse -Force
}
New-Item -ItemType File -Path (Join-Path $destination 'portable.flag') -Force | Out-Null
$sdk=Join-Path $root 'tools/dotnet/dotnet.exe'
if(!(Test-Path -LiteralPath $sdk)){$sdk='C:/Users/szajf/My Projects/ZenithClipper/tools/dotnet/dotnet.exe'}
if(!(Test-Path -LiteralPath $sdk)){$sdk=(Get-Command dotnet).Source}
& $sdk publish src/ZenithClipper.App -c Release --no-restore --no-build -p:ZenithLocalRuntime=true -o $destination
if($LASTEXITCODE){exit $LASTEXITCODE}
& $sdk publish src/ZenithClipper.Windows -c Release --no-restore --no-build -p:ZenithLocalRuntime=true -o $destination
if($LASTEXITCODE){exit $LASTEXITCODE}
$sdkRoot=Split-Path $sdk -Parent
$version=(Get-ChildItem (Join-Path $sdkRoot 'shared/Microsoft.WindowsDesktop.App') -Directory | Sort-Object { [Version]$_.Name } -Descending | Select-Object -First 1).Name
if(!$version){throw 'A local .NET Desktop runtime is required for packaging.'}
$runtime=Join-Path $destination 'runtime'
foreach($relative in @('host/fxr','shared/Microsoft.NETCore.App','shared/Microsoft.WindowsDesktop.App')){
    $parent=Join-Path $runtime $relative;New-Item -ItemType Directory -Path $parent -Force | Out-Null
    Copy-Item (Join-Path $sdkRoot "$relative/$version") $parent -Recurse -Force
}
Get-ChildItem $sdkRoot -File | Where-Object {$_.Name -match 'LICENSE|ThirdParty'} | Copy-Item -Destination $runtime -Force
if (!$FfmpegDirectory) {
    $binary = Get-ChildItem (Join-Path $root 'tools/ffmpeg-package') -Filter ffmpeg.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($binary) { $FfmpegDirectory = $binary.DirectoryName }
}
if ($FfmpegDirectory) {
    Copy-Item (Join-Path $FfmpegDirectory 'ffmpeg.exe') $destination -Force
    Copy-Item (Join-Path $FfmpegDirectory 'ffprobe.exe') $destination -Force
    $package = Split-Path $FfmpegDirectory -Parent
    New-Item -ItemType Directory -Path (Join-Path $destination 'licenses/ffmpeg') -Force | Out-Null
    Get-ChildItem $package -File | Copy-Item -Destination (Join-Path $destination 'licenses/ffmpeg') -Force
} else { Write-Warning 'FFmpeg and FFprobe must be on PATH or placed beside zenithclipper.exe.' }
& "$PSScriptRoot/build-native-recorder.ps1"
if($LASTEXITCODE){exit $LASTEXITCODE}
$nativePackage=Join-Path $root 'build/native-recorder-validation'
foreach($asset in @('ZenithClipper.Recorder.exe','nvenc_shim.dll','zenith_aac.dll')) {
    $sourceAsset=Join-Path $nativePackage $asset
    if(!(Test-Path -LiteralPath $sourceAsset)){throw "Missing native recorder asset: $asset"}
    Copy-Item -LiteralPath $sourceAsset -Destination $destination -Recurse -Force
}
$nvencNotice=Join-Path $destination 'licenses/nvenc'
New-Item -ItemType Directory -Path $nvencNotice -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $root 'src/ZenithClipper.Native.Video/native/nvEncodeAPI.h') -Destination $nvencNotice -Force
if ($PSVersionTable.PSVersion.Major -ge 7) { & "$PSScriptRoot/write-benchmarks.ps1" } else { Write-Host 'Skipping benchmark table (needs PowerShell 7).' }
Write-Host "Built: $destination/ZenithClipper.Windows.exe (native NVIDIA recorder included)"

& "$PSScriptRoot/build-editor.ps1"
if($LASTEXITCODE){exit $LASTEXITCODE}


