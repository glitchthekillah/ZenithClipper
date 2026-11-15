$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot)
$vc=Join-Path $repo 'tools/native-toolchain/msvc/VC/Tools/MSVC/14.44.35207'
$sdk=Join-Path $repo 'tools/native-toolchain/msvc/Windows Kits/10'
$env:PATH=(Join-Path $vc 'bin/Hostx64/x64')+';'+$env:PATH
$env:INCLUDE=(Join-Path $vc 'include')+';'+(Join-Path $sdk 'Include/10.0.26100.0/ucrt')+';'+(Join-Path $sdk 'Include/10.0.26100.0/shared')+';'+(Join-Path $sdk 'Include/10.0.26100.0/um')+';'+(Join-Path $repo 'tools/fdk-aac/source/libAACenc/include')+';'+(Join-Path $repo 'tools/fdk-aac/source/libSYS/include')
$env:LIB=(Join-Path $vc 'lib/x64')+';'+(Join-Path $sdk 'Lib/10.0.26100.0/ucrt/x64')+';'+(Join-Path $sdk 'Lib/10.0.26100.0/um/x64')
New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'FdkBuild') | Out-Null
& (Join-Path $vc 'bin/Hostx64/x64/cl.exe') /nologo /LD /MT /O2 /W4 /EHsc (Join-Path $PSScriptRoot 'FdkNative.cpp') (Join-Path $repo 'tools/fdk-aac/build/fdk-aac.lib') /Fo"$PSScriptRoot/FdkBuild/fdk_shim.obj" /link /MANIFEST:NO /OPT:REF /OPT:ICF /OUT:"$PSScriptRoot/FdkBuild/zenith_fdk_aac.dll" /IMPLIB:"$PSScriptRoot/FdkBuild/zenith_fdk_aac.lib"
exit $LASTEXITCODE
