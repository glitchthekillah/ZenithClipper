$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot)
$native = Join-Path $repo 'tools/native-toolchain/msvc'
$vc = Join-Path $native 'VC/Tools/MSVC/14.44.35207'
$sdk = Join-Path $native 'Windows Kits/10'
$env:PATH = (Join-Path $vc 'bin/Hostx64/x64') + ';' + $env:PATH
$env:INCLUDE = (Join-Path $vc 'include') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/ucrt') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/shared') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/um') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/winrt')
$env:LIB = (Join-Path $vc 'lib/x64') + ';' + (Join-Path $sdk 'Lib/10.0.26100.0/ucrt/x64') + ';' + (Join-Path $sdk 'Lib/10.0.26100.0/um/x64')
$output = Join-Path $PSScriptRoot 'bin/Release/net10.0-windows10.0.26100.0'
New-Item -ItemType Directory -Force -Path $output | Out-Null
& (Join-Path $vc 'bin/Hostx64/x64/cl.exe') /nologo /LD /MT /O2 /W4 (Join-Path $PSScriptRoot 'native/nvenc_shim.cpp') /Fo"$PSScriptRoot/native/nvenc_shim.obj" /link /OUT:"$output/nvenc_shim.dll" /IMPLIB:"$PSScriptRoot/native/nvenc_shim.lib" /PDB:"$PSScriptRoot/native/nvenc_shim.pdb"
exit $LASTEXITCODE
