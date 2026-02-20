@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
cd /d "%ROOT%"

set "ELEVATED_FLAG=%~1"
if /i not "%ELEVATED_FLAG%"=="--elevated" (
    call :EnsureAdmin
    set "ADMIN_STATUS=%errorlevel%"
    if "%ADMIN_STATUS%"=="2" exit /b 0
    if not "%ADMIN_STATUS%"=="0" exit /b %ADMIN_STATUS%
)

set "CORE_SCRIPT=%ROOT%scripts\Setup-InstantLoginSwitcher.ps1"
if not exist "%CORE_SCRIPT%" (
    echo Could not find core setup script:
    echo %CORE_SCRIPT%
    pause
    exit /b 1
)

set "PRIMARY_USER=Samuel Wunderly"
set "SECONDARY_USER=Lizzy Wunderly"

echo.
echo InstantLoginSwitcher installer
echo.
echo Leave blank to keep defaults shown in [brackets].
echo.

set /p "PRIMARY_INPUT=Primary user [%PRIMARY_USER%]: "
if defined PRIMARY_INPUT set "PRIMARY_USER=%PRIMARY_INPUT%"

set /p "SECONDARY_INPUT=Secondary user [%SECONDARY_USER%]: "
if defined SECONDARY_INPUT set "SECONDARY_USER=%SECONDARY_INPUT%"

echo.
echo Running install...
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command ^
  "$scriptPath = $env:CORE_SCRIPT; $scriptText = Get-Content -LiteralPath $scriptPath -Raw; $core = [ScriptBlock]::Create($scriptText); & $core -Mode Install -PrimaryUser $env:PRIMARY_USER -SecondaryUser $env:SECONDARY_USER"

set "EXIT_CODE=%errorlevel%"
echo.

if not "%EXIT_CODE%"=="0" (
    echo Install failed with exit code %EXIT_CODE%.
    echo.
    echo If this machine blocks scripts by policy, this installer still works because it runs in-memory.
    echo If it still fails, send the full text shown above.
    pause
    exit /b %EXIT_CODE%
)

echo Install completed.
echo Sign out and back in once, then press Numpad4 + Numpad5 + Numpad6 together.
pause
exit /b 0

:EnsureAdmin
set "IS_ADMIN=False"
for /f "usebackq delims=" %%I in (`powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$p = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent()); if ($p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { 'True' } else { 'False' }"`) do (
    set "IS_ADMIN=%%I"
)

if /i "%IS_ADMIN%"=="True" exit /b 0

set "SELF=%~f0"
set "SELF_PS=%SELF:'=''%"
echo Requesting Administrator permissions...
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "try { Start-Process -FilePath '%SELF_PS%' -Verb RunAs -ArgumentList '--elevated'; exit 0 } catch { exit 1 }"
if not "%errorlevel%"=="0" (
    echo Administrator elevation was cancelled or failed.
    pause
    exit /b 1
)

exit /b 2
