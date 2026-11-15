param([string]$NativeVideoBridgePath='')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$native=Join-Path $repo 'tools/native-toolchain/msvc'
$vc=Join-Path $native 'VC/Tools/MSVC/14.44.35207'
$sdk=Join-Path $native 'Windows Kits/10/Lib/10.0.26100.0'
$env:PATH=(Join-Path $vc 'bin/Hostx64/x64')+';'+$env:PATH
$env:LIB=(Join-Path $vc 'lib/x64')+';'+(Join-Path $sdk 'ucrt/x64')+';'+(Join-Path $sdk 'um/x64')
$env:DOTNET_CLI_HOME=Join-Path $repo '.build-home'
$env:APPDATA=Join-Path $repo '.build-home/AppData'
$env:NUGET_PACKAGES=Join-Path $repo '.packages'
$env:DOTNET_PROCESSOR_COUNT='2'
if(!$NativeVideoBridgePath){
    $bridgeDirectory=Join-Path $repo 'build/native-video'
    & (Join-Path $repo 'src/ZenithClipper.Native.Video/build-native.ps1') -OutputDirectory $bridgeDirectory
    if($LASTEXITCODE){exit $LASTEXITCODE}
    $NativeVideoBridgePath=Join-Path $bridgeDirectory 'nvenc_shim.dll'
}
if(!(Test-Path -LiteralPath $NativeVideoBridgePath)){throw 'The native WGC/NVENC bridge is missing.'}
$NativeVideoBridgePath=(Resolve-Path -LiteralPath $NativeVideoBridgePath).Path
& (Join-Path $repo 'tools/dotnet/dotnet.exe') publish (Join-Path $repo 'src/ZenithClipper.Recorder.Native') -c Release -r win-x64 -o (Join-Path $repo 'build/native-recorder-validation') /p:IlcUseEnvironmentalTools=true "-p:NativeVideoBridgePath=$NativeVideoBridgePath" -m:1
exit $LASTEXITCODE
