$ErrorActionPreference = 'Stop'
$repo = 'C:/Users/szajf/My Projects/ZenithClipper'
$native = Join-Path $repo 'tools/native-toolchain/msvc'
$vc = Join-Path $native 'VC/Tools/MSVC/14.44.35207'
$sdk = Join-Path $native 'Windows Kits/10'
$env:PATH = (Join-Path $vc 'bin/Hostx64/x64') + ';' + $env:PATH
$env:INCLUDE = (Join-Path $vc 'include') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/ucrt') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/shared') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/um') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/winrt')
$env:LIB = (Join-Path $vc 'lib/x64') + ';' + (Join-Path $sdk 'Lib/10.0.26100.0/ucrt/x64') + ';' + (Join-Path $sdk 'Lib/10.0.26100.0/um/x64')
$env:INCLUDE += ';' + (Join-Path $sdk 'Include/10.0.26100.0/cppwinrt')
& (Join-Path $vc 'bin/Hostx64/x64/cl.exe') /nologo /LD /MT /O2 /W4 /DNC_TEST_HOOKS (Join-Path $PSScriptRoot 'verified_wait_shim.cpp') /Fo"$PSScriptRoot/verified-poll.obj" /link /OUT:"$PSScriptRoot/verified-poll.dll" /IMPLIB:"$PSScriptRoot/verified-poll.lib"
if($LASTEXITCODE){exit $LASTEXITCODE}
& (Join-Path $vc 'bin/Hostx64/x64/cl.exe') /nologo /LD /MT /O2 /W4 /DNC_TEST_HOOKS /DNC_EVENT_WAIT (Join-Path $PSScriptRoot 'verified_wait_shim.cpp') /Fo"$PSScriptRoot/verified-event.obj" /link /OUT:"$PSScriptRoot/verified-event.dll" /IMPLIB:"$PSScriptRoot/verified-event.lib"
exit $LASTEXITCODE


