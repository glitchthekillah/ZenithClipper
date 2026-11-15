$ErrorActionPreference = 'Stop'
$repo = 'C:/Users/szajf/My Projects/ZenithClipper'
$native = Join-Path $repo 'tools/native-toolchain/msvc'
$vc = Join-Path $native 'VC/Tools/MSVC/14.44.35207'
$sdk = Join-Path $native 'Windows Kits/10'
$env:PATH = (Join-Path $vc 'bin/Hostx64/x64') + ';' + $env:PATH
$env:INCLUDE = (Join-Path $vc 'include') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/ucrt') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/shared') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/um') + ';' + (Join-Path $sdk 'Include/10.0.26100.0/winrt')
$env:LIB = (Join-Path $vc 'lib/x64') + ';' + (Join-Path $sdk 'Lib/10.0.26100.0/ucrt/x64') + ';' + (Join-Path $sdk 'Lib/10.0.26100.0/um/x64')
$env:INCLUDE += ';' + (Join-Path $sdk 'Include/10.0.26100.0/cppwinrt')
foreach($variant in @('cleanup-base','cleanup-clear')){
    $output=Join-Path $PSScriptRoot $variant
    $source=if($variant -eq 'cleanup-base'){Join-Path $repo 'src/ZenithClipper.Native.Video/native/nvenc_shim.cpp'}else{Join-Path $PSScriptRoot 'nvenc_cleanup.cpp'}
    & (Join-Path $vc 'bin/Hostx64/x64/cl.exe') /nologo /LD /std:c++20 /EHsc /MT /O2 /W4 /I"$repo/src/ZenithClipper.Native.Video/native" $source (Join-Path $repo 'src/ZenithClipper.Native.Video/native/wgc_capture.cpp') /Fo"$output/" /link /OUT:"$output/nvenc_shim.dll" /IMPLIB:"$output/nvenc_shim.lib" d3d11.lib dxgi.lib windowsapp.lib user32.lib
    if($LASTEXITCODE){exit $LASTEXITCODE}
}
