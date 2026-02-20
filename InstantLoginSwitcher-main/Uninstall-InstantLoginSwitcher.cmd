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

echo.
echo Running uninstall...
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command ^
  "$scriptPath = $env:CORE_SCRIPT; $scriptText = Get-Content -LiteralPath $scriptPath -Raw; $core = [ScriptBlock]::Create($scriptText); & $core -Mode Uninstall"

set "EXIT_CODE=%errorlevel%"
echo.

if not "%EXIT_CODE%"=="0" (
    echo Uninstall failed with exit code %EXIT_CODE%.
    echo Send the full text shown above.
    pause
    exit /b %EXIT_CODE%
)

echo Uninstall completed.
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
