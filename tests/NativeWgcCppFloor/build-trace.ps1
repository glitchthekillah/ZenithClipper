$ErrorActionPreference = 'Stop'
$repo = 'C:/Users/szajf/My Projects/ZenithClipper'
$native = Join-Path $repo 'tools/native-toolchain/msvc'
$vc = Join-Path $native 'VC/Tools/MSVC/14.44.35207'
$sdk = Join-Path $native 'Windows Kits/10'
$env:PATH = (Join-Path $vc 'bin/Hostx64/x64') + ';' + $env:PATH
$env:INCLUDE = (Join-Path $vc 'include') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/ucrt') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/shared') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/um') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/winrt')
$env:LIB = (Join-Path $vc 'lib/x64') + ';' + (Join-Path $sdk 'Lib/10.0.26100.0/ucrt/x64') + ';' + (Join-Path $sdk 'Lib/10.0.26100.0/um/x64')
$env:INCLUDE += ';' + (Join-Path $sdk 'Include/10.0.26100.0/cppwinrt')
& (Join-Path $vc 'bin/Hostx64/x64/cl.exe') /nologo /LD /MT /O2 /W4 /I"$repo/src/ZenithClipper.Native.Video/native" (Join-Path $PSScriptRoot 'fence-trace.cpp') /Fo"$PSScriptRoot/fence-trace.obj" /link /OUT:"$PSScriptRoot/fence-trace.dll" /IMPLIB:"$PSScriptRoot/fence-trace.lib"
exit $LASTEXITCODE

