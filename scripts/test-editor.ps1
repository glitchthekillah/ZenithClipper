#requires -Version 7.0
param([switch]$SkipBuild, [switch]$SkipNative, [ValidateRange(1,45)][int]$WatchdogSeconds = 45, [string]$SnapshotPath = '')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$sdk = Join-Path $root 'tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $root '.build-home'
$env:APPDATA = Join-Path $env:DOTNET_CLI_HOME 'AppData'
$env:NUGET_PACKAGES = Join-Path $root '.packages'

function Invoke-BoundedEditorProcess([string]$Executable, [string[]]$Arguments, [string]$Label, [string]$MltRoot = '') {
    $info = [Diagnostics.ProcessStartInfo]::new($Executable)
    $info.WorkingDirectory = $root
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    # Child-only environment: native tests never open the user's audio device.
    $info.Environment['SDL_AUDIODRIVER'] = 'dummy'
    $info.Environment['SDL_VIDEODRIVER'] = 'dummy'
    $info.Environment['QT_QPA_PLATFORM'] = 'offscreen'
    if ($MltRoot) { $info.Environment['ZENITHCLIPPER_MLT_ROOT'] = $MltRoot }
    $process = [Diagnostics.Process]::Start($info)
    if (!$process) { throw "Could not start $Label." }
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit($WatchdogSeconds * 1000)) {
            # Kill only this process and its own children; never match other desktop processes.
            try { $process.Kill($true) } catch [InvalidOperationException] { }
            if (!$process.WaitForExit(5000)) { throw "$Label exceeded the watchdog and its process did not exit after termination." }
            throw "$Label exceeded its $WatchdogSeconds second watchdog; its own process tree was stopped."
        }
        $output = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        if ($output) { Write-Host $output.TrimEnd() }
        if ($errorText) { Write-Host $errorText.TrimEnd() }
        if ($process.ExitCode) { throw "$Label failed with exit code $($process.ExitCode)." }
        Write-Host "PASS $Label"
    } finally { $process.Dispose() }
}

$editingProject = Join-Path $root 'tests/ZenithClipper.Editing.Tests/ZenithClipper.Editing.Tests.csproj'
$nativeProject = Join-Path $root 'tests/ZenithClipper.Mlt.Tests/ZenithClipper.Mlt.Tests.csproj'
$desktopProject = Join-Path $root 'src/ZenithClipper.Editor.Desktop/ZenithClipper.Editor.Desktop.csproj'
if (!$SkipBuild) {
    foreach ($project in @($editingProject, $nativeProject, $desktopProject)) {
        Invoke-BoundedEditorProcess $sdk @('build', $project, '-c', 'Release', '--no-restore', '-v', 'minimal') "Build $([IO.Path]::GetFileNameWithoutExtension($project))"
    }
}
Invoke-BoundedEditorProcess $sdk @((Join-Path $root 'tests/ZenithClipper.Editing.Tests/bin/Release/net10.0/ZenithClipper.Editing.Tests.dll')) 'Editing domain tests'
$published = Join-Path $root 'build/editor/ZenithClipper.Editor.exe'
$smokeArguments = @('--headless-smoke')
if ($SnapshotPath) {
    if (![IO.Path]::IsPathFullyQualified($SnapshotPath)) { throw 'SnapshotPath must be an absolute PNG output path.' }
    $smokeArguments += @('--snapshot', $SnapshotPath)
}
if (Test-Path -LiteralPath $published) {
    Invoke-BoundedEditorProcess $published $smokeArguments 'Published apphost headless UI/title/preview/FPS smoke'
} else {
    Invoke-BoundedEditorProcess $sdk (@((Join-Path $root 'src/ZenithClipper.Editor.Desktop/bin/Release/net10.0/ZenithClipper.Editor.dll')) + $smokeArguments) 'Headless UI/title/preview/FPS smoke'
}
if (!$SkipNative) {
    $mlt = Join-Path $root 'build/editor/mlt'
    if (!(Test-Path -LiteralPath (Join-Path $mlt 'libmlt-7.dll'))) { $mlt = Join-Path $root 'tools/mlt-runtime/package/Shotcut' }
    $ffmpeg = Join-Path $root 'build/windows/ffmpeg.exe'
    if (!(Test-Path -LiteralPath $ffmpeg)) { $ffmpeg = Join-Path $root 'tools/mlt-runtime/package/Shotcut/ffmpeg.exe' }
    if (!(Test-Path -LiteralPath $ffmpeg)) { throw 'Fixture FFmpeg is missing.' }
    $mutex = [Threading.Mutex]::new($false, 'Local\ZenithClipper.Editor.NativeTests')
    $owned = $false
    try {
        try { $owned = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned = $true }
        if (!$owned) { throw 'Another editor native fixture process is already running; retry when it finishes.' }
        Invoke-BoundedEditorProcess $sdk @((Join-Path $root 'tests/ZenithClipper.Mlt.Tests/bin/Release/net10.0/ZenithClipper.Mlt.Tests.dll'), $ffmpeg) 'Single native MLT fixture suite with dummy SDL' $mlt
    } finally { if ($owned) { $mutex.ReleaseMutex() }; $mutex.Dispose() }
}
Write-Host 'Editor checks finished. No visible desktop UI, capture, or real audio device was opened.'
