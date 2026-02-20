@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
cd /d "%ROOT%"

set "ELEVATION_ATTEMPTED=0"
if /i "%~1"=="--elevated" (
    set "ELEVATION_ATTEMPTED=1"
    shift /1
)

call :EnsureAdmin
set "ENSURE_ADMIN_EXIT_CODE=%errorlevel%"
if "%ENSURE_ADMIN_EXIT_CODE%"=="2" exit /b 0
if not "%ENSURE_ADMIN_EXIT_CODE%"=="0" exit /b %ENSURE_ADMIN_EXIT_CODE%

set "SETUP_SCRIPT=%ROOT%scripts\Setup-InstantLoginSwitcher.ps1"

if not exist "%SETUP_SCRIPT%" (
    echo Could not find setup script:
    echo %SETUP_SCRIPT%
    pause
    exit /b 1
)

set "PRIMARY_USER=Samuel Wunderly"
set "SECONDARY_USER=Lizzy Wunderly"

echo.
echo InstantLoginSwitcher one-click setup
echo Run from any unzipped folder location.
echo Leave blank to keep defaults shown in [brackets].
echo.

call :UnblockDownloadedFiles

set /p "PRIMARY_INPUT=Primary user [%PRIMARY_USER%]: "
if defined PRIMARY_INPUT set "PRIMARY_USER=%PRIMARY_INPUT%"

set /p "SECONDARY_INPUT=Secondary user [%SECONDARY_USER%]: "
if defined SECONDARY_INPUT set "SECONDARY_USER=%SECONDARY_INPUT%"

echo.
echo Running setup...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SETUP_SCRIPT%" -PrimaryUser "%PRIMARY_USER%" -SecondaryUser "%SECONDARY_USER%"
set "EXIT_CODE=%errorlevel%"

echo.
if not "%EXIT_CODE%"=="0" (
    echo Setup failed with exit code %EXIT_CODE%.
    echo Check the PowerShell error shown above.
    pause
    exit /b %EXIT_CODE%
)

echo Setup complete.
echo Sign out and sign back in once, then test Numpad4 + Numpad5 + Numpad6.
pause
exit /b 0

:EnsureAdmin
set "IS_ADMIN=False"
for /f "usebackq delims=" %%I in (`powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent()); if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { 'True' } else { 'False' }"`) do (
    set "IS_ADMIN=%%I"
)

if /i "%IS_ADMIN%"=="True" exit /b 0

if "%ELEVATION_ATTEMPTED%"=="1" (
    echo Failed to run as Administrator. Please right-click this file and choose "Run as administrator".
    pause
    exit /b 1
)

set "SELF=%~f0"
set "SELF_PS=%SELF:'=''%"
echo Requesting Administrator permissions...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "try { Start-Process -FilePath '%SELF_PS%' -Verb RunAs -ArgumentList '--elevated'; exit 0 } catch { exit 1 }"
if not "%errorlevel%"=="0" (
    echo Administrator elevation was cancelled or failed.
    pause
    exit /b 1
)

exit /b 2

:UnblockDownloadedFiles
REM Safe no-op if files are already unblocked.
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$root = $env:ROOT; if (Test-Path -LiteralPath $root) { Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in '.ps1','.psm1','.ahk','.cmd' } | Unblock-File -ErrorAction SilentlyContinue }" >nul 2>&1
exit /b 0
