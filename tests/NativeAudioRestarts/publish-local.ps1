$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot)
$native=Join-Path $repo 'tools/native-toolchain/msvc'
$vc=Join-Path $native 'VC/Tools/MSVC/14.44.35207'
$sdk=Join-Path $native 'Windows Kits/10/Lib/10.0.26100.0'
$env:PATH=(Join-Path $vc 'bin/Hostx64/x64')+';'+$env:PATH
$env:LIB=(Join-Path $vc 'lib/x64')+';'+(Join-Path $sdk 'ucrt/x64')+';'+(Join-Path $sdk 'um/x64')
$env:DOTNET_CLI_HOME=Join-Path $repo '.build-home'
$env:APPDATA=Join-Path $repo '.build-home/AppData'
$env:NUGET_PACKAGES=Join-Path $repo '.packages'
$env:DOTNET_PROCESSOR_COUNT='2'
& (Join-Path $repo 'tools/dotnet/dotnet.exe') publish $PSScriptRoot -c Release -r win-x64 -o (Join-Path $PSScriptRoot 'publish') /p:IlcUseEnvironmentalTools=true -m:1
exit $LASTEXITCODE

