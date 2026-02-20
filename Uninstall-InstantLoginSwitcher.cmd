@echo off
setlocal EnableExtensions

cd /d "%~dp0"
set "LOG_FILE=%TEMP%\InstantLoginSwitcher-uninstall.log"

echo ============================================== > "%LOG_FILE%"
echo InstantLoginSwitcher uninstall started %DATE% %TIME% >> "%LOG_FILE%"
echo Working directory: %CD% >> "%LOG_FILE%"

echo InstantLoginSwitcher uninstaller
echo.
echo Log file: %LOG_FILE%
echo.

net session >nul 2>&1
if not "%errorlevel%"=="0" (
    echo This uninstaller must be run as Administrator.
    echo Please right-click this file and choose "Run as administrator".
    echo ERROR: not running as admin >> "%LOG_FILE%"
    echo.
    pause
    exit /b 1
)

set "CORE_SCRIPT=%CD%\scripts\Setup-InstantLoginSwitcher.ps1"
if not exist "%CORE_SCRIPT%" (
    echo Could not find core setup script:
    echo %CORE_SCRIPT%
    echo ERROR: missing core script >> "%LOG_FILE%"
    echo.
    pause
    exit /b 1
)

echo Running in-memory PowerShell core... >> "%LOG_FILE%"

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; try { $scriptPath = $env:CORE_SCRIPT; $scriptText = Get-Content -LiteralPath $scriptPath -Raw; $core = [ScriptBlock]::Create($scriptText); & $core -Mode Uninstall; exit 0 } catch { $_ | Out-String | Write-Host; $_ | Out-String | Out-File -FilePath $env:LOG_FILE -Append -Encoding utf8; exit 1 }" >> "%LOG_FILE%" 2>&1

set "EXIT_CODE=%errorlevel%"
echo PowerShell exit code: %EXIT_CODE% >> "%LOG_FILE%"
echo.

if not "%EXIT_CODE%"=="0" (
    echo Uninstall failed with exit code %EXIT_CODE%.
    echo See log: %LOG_FILE%
    echo.
    pause
    exit /b %EXIT_CODE%
)

echo Uninstall completed.
echo See log: %LOG_FILE%
echo.
pause
exit /b 0
