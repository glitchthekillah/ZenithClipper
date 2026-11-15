# Downloads whisper.cpp (whisper-cli) and a speech model into tools\whisper for the editor's auto captions.
# Run once:  powershell -ExecutionPolicy Bypass -File scripts\get-whisper.ps1
# Options:   -Model tiny|base|small   (base ~148 MB is the default; small ~488 MB is more accurate, slower)
#            -Cuda                     (NVIDIA build; much faster on an RTX card, larger download)
# Captions are made locally. The editor never downloads anything itself and no audio leaves the PC.
param(
    [ValidateSet('tiny', 'base', 'small')][string]$Model = 'base',
    [switch]$Cuda
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root 'tools\whisper'
$models = Join-Path $dest 'models'
New-Item -ItemType Directory -Force -Path $dest, $models | Out-Null

$tag = 'b5454'
$asset = if ($Cuda) { 'whisper-bin-win-cuda-12.4.0-x64.zip' } else { 'whisper-bin-x64.zip' }
if (-not (Test-Path (Join-Path $dest 'whisper-cli.exe'))) {
    $zip = Join-Path $env:TEMP "zenith-$asset"
    $unpacked = Join-Path $env:TEMP "zenith-whisper-$tag"
    Write-Host "Downloading whisper.cpp $tag ($asset)..."
    Invoke-WebRequest "https://github.com/ggml-org/whisper.cpp/releases/download/$tag/$asset" -OutFile $zip
    Remove-Item $unpacked -Recurse -Force -ErrorAction SilentlyContinue
    Expand-Archive $zip $unpacked
    $cli = Get-ChildItem $unpacked -Recurse -Filter 'whisper-cli.exe' | Select-Object -First 1
    if (-not $cli) { throw "whisper-cli.exe was not found inside $asset." }
    Copy-Item (Join-Path $cli.DirectoryName '*') $dest -Recurse -Force
    Remove-Item $zip, $unpacked -Recurse -Force -ErrorAction SilentlyContinue
}

$modelFile = Join-Path $models "ggml-$Model.bin"
if (-not (Test-Path $modelFile)) {
    Write-Host "Downloading the $Model speech model..."
    $partial = "$modelFile.part"
    Invoke-WebRequest "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-$Model.bin" -OutFile $partial
    Move-Item $partial $modelFile -Force
}
if (-not (Test-Path (Join-Path $dest 'whisper-cli.exe'))) { throw "whisper-cli.exe is missing from $dest." }
Write-Host "Auto captions ready: $dest"
