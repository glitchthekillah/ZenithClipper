param([string]$Task = 'test')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$env:DOTNET_CLI_HOME = Join-Path $root '.build-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_PACKAGES = Join-Path $root '.packages'
$env:APPDATA = Join-Path $root '.build-home/AppData'
New-Item -ItemType Directory -Path $env:APPDATA -Force | Out-Null
$sdk = Join-Path $root 'tools/dotnet/dotnet.exe'
if (!(Test-Path $sdk)) { $sdk = 'C:/Users/szajf/My Projects/ZenithClipper/tools/dotnet/dotnet.exe' }
if (!(Test-Path $sdk)) { $sdk = 'dotnet' }
$project = if ($Task -eq 'test') { 'tests/ZenithClipper.Tests' } elseif ($Task -eq 'panel') { 'src/ZenithClipper.Windows' } else { 'src/ZenithClipper.App' }
& $sdk restore $project --configfile NuGet.Config
if ($LASTEXITCODE) { exit $LASTEXITCODE }
if ($Task -eq 'test') { & $sdk run --no-restore --project $project } else { & $sdk build --no-restore $project -c Release }
exit $LASTEXITCODE
