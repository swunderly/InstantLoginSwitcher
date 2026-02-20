@echo off
setlocal EnableExtensions EnableDelayedExpansion

cd /d "%~dp0"
set "LOG_FILE=%TEMP%\InstantLoginSwitcher-install.log"

echo ============================================== > "!LOG_FILE!"
echo InstantLoginSwitcher install started %DATE% %TIME% >> "!LOG_FILE!"
echo Working directory: %CD% >> "!LOG_FILE!"

echo InstantLoginSwitcher installer
echo.
echo Log file: !LOG_FILE!
echo.

net session >nul 2>&1
if not "!errorlevel!"=="0" (
    echo This installer must be run as Administrator.
    echo Please right-click this file and choose "Run as administrator".
    echo ERROR: not running as admin >> "!LOG_FILE!"
    echo.
    pause
    exit /b 1
)

set "CORE_SCRIPT=%CD%\scripts\Setup-InstantLoginSwitcher.ps1"
if not exist "!CORE_SCRIPT!" (
    echo Could not find core setup script:
    echo !CORE_SCRIPT!
    echo ERROR: missing core script >> "!LOG_FILE!"
    echo.
    pause
    exit /b 1
)

set "ILS_PRIMARY_USER=Samuel Wunderly"
set "ILS_SECONDARY_USER=Lizzy Wunderly"

echo Leave blank to keep defaults shown in [brackets].
echo.
set /p "PRIMARY_INPUT=Primary user [%ILS_PRIMARY_USER%]: "
if defined PRIMARY_INPUT set "ILS_PRIMARY_USER=%PRIMARY_INPUT%"

set /p "SECONDARY_INPUT=Secondary user [%ILS_SECONDARY_USER%]: "
if defined SECONDARY_INPUT set "ILS_SECONDARY_USER=%SECONDARY_INPUT%"

echo Primary user: !ILS_PRIMARY_USER! >> "!LOG_FILE!"
echo Secondary user: !ILS_SECONDARY_USER! >> "!LOG_FILE!"
echo Running in-memory PowerShell core... >> "!LOG_FILE!"

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; try { $scriptPath = $env:CORE_SCRIPT; $scriptText = Get-Content -LiteralPath $scriptPath -Raw; $core = [ScriptBlock]::Create($scriptText); & $core -Mode Install -PrimaryUser $env:ILS_PRIMARY_USER -SecondaryUser $env:ILS_SECONDARY_USER; exit 0 } catch { Write-Host $_.Exception.Message; Add-Content -LiteralPath $env:LOG_FILE -Value $_.Exception.ToString(); exit 1 }" >> "!LOG_FILE!" 2>&1

set "EXIT_CODE=%errorlevel%"
echo PowerShell exit code: !EXIT_CODE! >> "!LOG_FILE!"
echo.

if not "!EXIT_CODE!"=="0" (
    echo Install failed with exit code !EXIT_CODE!.
    echo See log: !LOG_FILE!
    echo.
    pause
    exit /b !EXIT_CODE!
)

echo Install completed.
echo Sign out and back in once, then press Numpad4 + Numpad5 + Numpad6 together.
echo See log: !LOG_FILE!
echo.
pause
exit /b 0
