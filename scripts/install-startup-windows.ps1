param([switch]$Remove)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'build/windows/ZenithClipper.Windows.exe'
$folder = [Environment]::GetFolderPath('Startup')
$linkPath = Join-Path $folder 'ZenithClipper.lnk'
if ($Remove) { if (Test-Path -LiteralPath $linkPath) { Remove-Item -LiteralPath $linkPath }; Write-Host 'ZenithClipper startup disabled. Saved clips and settings are preserved.'; exit }
if (!(Test-Path -LiteralPath $exe)) { throw 'Build the Windows package first.' }
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($linkPath)
$link.TargetPath = $exe
$link.Arguments = '--background'
$link.WorkingDirectory = Split-Path $exe -Parent
$link.Description = 'Start the ZenithClipper recording engine without opening the panel or editor.'
$link.Save()
Write-Host 'ZenithClipper will start capturing at login. Run this script with -Remove to disable.'
