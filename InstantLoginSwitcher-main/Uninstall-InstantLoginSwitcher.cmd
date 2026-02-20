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
    echo Run this uninstaller from the unzipped InstantLoginSwitcher folder.
    pause
    exit /b 1
)

REM Unblock files from downloaded ZIP (safe no-op when already unblocked).
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -Path '%ROOT%*' -Recurse -File -Include *.ps1,*.psm1,*.ahk,*.cmd | Unblock-File -ErrorAction SilentlyContinue" >nul 2>&1

echo.
echo Running uninstall...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SETUP_SCRIPT%" -Uninstall
set "EXIT_CODE=%errorlevel%"

echo.
if not "%EXIT_CODE%"=="0" (
    echo Uninstall failed with exit code %EXIT_CODE%.
    echo Check the PowerShell error shown above.
    pause
    exit /b %EXIT_CODE%
)

echo Uninstall complete.
pause
exit /b 0
