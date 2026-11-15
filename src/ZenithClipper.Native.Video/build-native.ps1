param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot)
$native = Join-Path $repo 'tools/native-toolchain/msvc'
$vc = Join-Path $native 'VC/Tools/MSVC/14.44.35207'
$sdk = Join-Path $native 'Windows Kits/10'
$env:PATH = (Join-Path $vc 'bin/Hostx64/x64') + ';' + $env:PATH
$env:INCLUDE = (Join-Path $vc 'include') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/ucrt') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/shared') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/um') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/winrt') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/cppwinrt')
$env:LIB = (Join-Path $vc 'lib/x64') + ';' + (Join-Path $sdk 'Lib/10.0.26100.0/ucrt/x64') + ';' + (Join-Path $sdk 'Lib/10.0.26100.0/um/x64')
if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot 'native' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
& (Join-Path $vc 'bin/Hostx64/x64/cl.exe') /nologo /LD /std:c++20 /EHsc /MT /O2 /W4 (Join-Path $PSScriptRoot 'native/nvenc_shim.cpp') (Join-Path $PSScriptRoot 'native/wgc_capture.cpp') /Fo"$OutputDirectory/" /link /OUT:"$OutputDirectory/nvenc_shim.dll" /IMPLIB:"$OutputDirectory/nvenc_shim.lib" /PDB:"$OutputDirectory/nvenc_shim.pdb" d3d11.lib dxgi.lib windowsapp.lib user32.lib
exit $LASTEXITCODE
