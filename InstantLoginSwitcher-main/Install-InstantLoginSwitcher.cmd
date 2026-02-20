@echo off
setlocal EnableExtensions

REM Re-run this script as Administrator if needed.
fltmc >nul 2>&1
if not "%errorlevel%"=="0" (
    echo Requesting Administrator permissions...
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

set "ROOT=%~dp0"
cd /d "%ROOT%"
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

REM Unblock files from downloaded ZIP (safe no-op when already unblocked).
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -Path '%ROOT%*' -Recurse -File -Include *.ps1,*.psm1,*.ahk,*.cmd | Unblock-File -ErrorAction SilentlyContinue" >nul 2>&1

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
