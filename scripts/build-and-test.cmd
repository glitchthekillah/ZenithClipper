@echo off
rem Rebuilds the portable package and runs tests. Stops the engine first (it locks build\windows), restarts it after if it was running.
cd /d "%~dp0.."
set "LOG=%CD%\build-log.txt"
set WAS=0
echo === build-and-test %date% %time% > "%LOG%"
if exist build\windows\zenithclipper.exe (
  build\windows\zenithclipper.exe status >nul 2>&1 && set WAS=1
  build\windows\zenithclipper.exe shutdown >> "%LOG%" 2>&1
)
echo engine-was-running=%WAS% >> "%LOG%"
taskkill /im ZenithClipper.Windows.exe /f >nul 2>&1
timeout /t 3 /nobreak >nul
echo === core tests >> "%LOG%"
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\dev.ps1 test >> "%LOG%" 2>&1
echo TESTEXIT=%ERRORLEVEL% >> "%LOG%"
echo === package >> "%LOG%"
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\package-windows.ps1 >> "%LOG%" 2>&1
echo PACKEXIT=%ERRORLEVEL% >> "%LOG%"
echo === windows ui tests >> "%LOG%"
set DOTNET_CLI_HOME=%CD%\.build-home
set NUGET_PACKAGES=%CD%\.packages
tools\dotnet\dotnet.exe run -c Release --project tests\ZenithClipper.Windows.Tests >> "%LOG%" 2>&1
echo UITESTEXIT=%ERRORLEVEL% >> "%LOG%"
if "%WAS%"=="1" start "" build\windows\ZenithClipper.Windows.exe --background
echo === DONE %time% >> "%LOG%"
