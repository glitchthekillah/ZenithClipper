#requires -Version 5.1
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$sdk = Join-Path $root 'tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $root '.build-home'
$env:APPDATA = Join-Path $env:DOTNET_CLI_HOME 'AppData'
$env:NUGET_PACKAGES = Join-Path $root '.packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$project = Join-Path $root 'src/ZenithClipper.Editor.Desktop/ZenithClipper.Editor.Desktop.csproj'
$destination = Join-Path $root 'build/editor'
$source = Join-Path $root 'tools/mlt-runtime/package/Shotcut'
if (!(Test-Path -LiteralPath (Join-Path $source 'libmlt-7.dll'))) { throw 'The portable MLT source runtime is missing from tools/mlt-runtime/package/Shotcut.' }
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Push-Location $root
try {
    & $sdk restore $project --configfile (Join-Path $root 'NuGet.Config') -r win-x64 -p:SelfContained=true
    if ($LASTEXITCODE) { throw "Editor restore failed ($LASTEXITCODE)." }
    & $sdk publish $project -c Release -r win-x64 --self-contained true --no-restore -o $destination
    if ($LASTEXITCODE) { throw "Editor publish failed ($LASTEXITCODE)." }
} finally { Pop-Location }

$mlt = Join-Path $destination 'mlt'
New-Item -ItemType Directory -Force -Path $mlt | Out-Null
# Qt UI plugins are denied by the native loader; do not distribute the Shotcut UI.
Get-ChildItem -LiteralPath $source -File -Filter '*.dll' |
    Where-Object { $_.Name -notlike 'Qt6*' -and $_.Name -notin @('libCuteLogger.dll', 'opengl32sw.dll') } |
    Copy-Item -Destination $mlt -Force
foreach ($relative in @('lib/mlt', 'lib/frei0r-1', 'lib/ladspa', 'share/mlt', 'share/ffmpeg', 'share/vmaf', 'share/movit')) {
    $from = Join-Path $source $relative
    if (!(Test-Path -LiteralPath $from)) { continue }
    $to = Join-Path $mlt $relative
    New-Item -ItemType Directory -Force -Path $to | Out-Null
    Get-ChildItem -LiteralPath $from -Force | Copy-Item -Destination $to -Recurse -Force
}
$notices = Join-Path $destination 'licenses/mlt-runtime'
New-Item -ItemType Directory -Force -Path $notices | Out-Null
Get-ChildItem -LiteralPath $source -File |
    Where-Object { $_.Name -match '^(COPYING|LICENSE|NOTICE|AUTHORS|ThirdParty)' } |
    Copy-Item -Destination $notices -Force
Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY.md') -Destination $destination -Force
$draft = Join-Path $root 'docs/editor-rebuild/THIRD_PARTY_DRAFT.md'
if (Test-Path -LiteralPath $draft) { Copy-Item -LiteralPath $draft -Destination $notices -Force }
@'
MLT runtime: extracted portable Shotcut distribution in tools/mlt-runtime/package/Shotcut.
The package includes MLT/FFmpeg and their native dependency libraries, plugin/data folders,
and original license notices. Shotcut executables, Qt UI libraries, QML and UI data are excluded.
Upstream: https://www.mltframework.org/ and https://shotcut.org/
Keep original license notices with any redistributed package. No source files are removed by this script.
'@ | Set-Content -LiteralPath (Join-Path $notices 'PACKAGE-NOTES.txt') -Encoding utf8
@"
ZenithClipper editor private development package
Generated UTC: $([DateTime]::UtcNow.ToString('o'))
Managed UI: Avalonia 12.1.5; framework-dependent .NET 10; win-x64.
The editor includes its own .NET runtime.
Native runtime input: tools/mlt-runtime/package/Shotcut, extracted from the official Shotcut portable distribution.
Current source provenance and release URL: THIRD_PARTY.md and licenses/mlt-runtime/THIRD_PARTY_DRAFT.md.
Original native licenses: licenses/mlt-runtime/COPYING.txt and LICENSE.
Native inventory and hashes: licenses/mlt-runtime/RUNTIME-COMPONENTS.tsv.
Restored NuGet license metadata: licenses/NUGET-LICENSES.tsv; copied notices under licenses/nuget.
No Shotcut executables, Qt/QML application UI or Windows recorder user data are copied into the native runtime folder.
This inventory records observed package inputs. It is not a complete public-distribution licensing audit or source offer.
Outstanding notice/source obligations are recorded in THIRD_PARTY_DRAFT.md.
"@ | Set-Content -LiteralPath (Join-Path $destination 'PROVENANCE.txt') -Encoding utf8
Get-ChildItem -LiteralPath $mlt -Recurse -File | Sort-Object FullName | ForEach-Object {
    [PSCustomObject]@{
        Path = $_.FullName.Substring($mlt.Length + 1).Replace('\', '/')
        Bytes = $_.Length
        SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
} | Export-Csv -LiteralPath (Join-Path $notices 'RUNTIME-COMPONENTS.tsv') -Delimiter "`t" -NoTypeInformation -Encoding utf8

$assetsPath = Join-Path $root 'src/ZenithClipper.Editor.Desktop/obj/project.assets.json'
$licenseRows = @()
if (Test-Path -LiteralPath $assetsPath) {
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package') { continue }
        $parts = $library.Name.Split('/')
        $folder = Join-Path $env:NUGET_PACKAGES $library.Value.path
        $nuspec = Get-ChildItem -LiteralPath $folder -File -Filter '*.nuspec' -ErrorAction SilentlyContinue | Select-Object -First 1
        $licenseType = ''; $licenseValue = ''; $projectUrl = ''
        if ($nuspec) {
            [xml]$spec = Get-Content -LiteralPath $nuspec.FullName -Raw
            $metadata = $spec.package.metadata
            if ($metadata.license) { $licenseType = $metadata.license.GetAttribute('type'); $licenseValue = $metadata.license.InnerText }
            $projectUrl = [string]$metadata.projectUrl
            $packageNotices = Get-ChildItem -LiteralPath $folder -File | Where-Object { $_.Name -match '^(LICENSE|COPYING|NOTICE|Third[-_ ]?Party[-_ ]?Notices?)' }
            if ($packageNotices) {
                $to = Join-Path $destination ('licenses/nuget/' + $parts[0] + '/' + $parts[1])
                New-Item -ItemType Directory -Force -Path $to | Out-Null
                $packageNotices | Copy-Item -Destination $to -Force
            }
        }
        $licenseRows += [PSCustomObject]@{ Package = $parts[0]; Version = $parts[1]; LicenseType = $licenseType; License = $licenseValue; ProjectUrl = $projectUrl }
    }
}
$licenseRows | Sort-Object Package,Version | Export-Csv -LiteralPath (Join-Path $destination 'licenses/NUGET-LICENSES.tsv') -Delimiter "`t" -NoTypeInformation -Encoding utf8
if (Get-ChildItem -LiteralPath $mlt -Recurse -File -Filter '*.exe') { throw 'Unexpected executable in the MLT runtime folder; no existing files were deleted.' }
Write-Host "Built $destination/ZenithClipper.Editor.exe"
Write-Host 'Keep build/editor beside build/windows: the controls locate the editor there, and media import can use ../windows/ffprobe.exe.'
