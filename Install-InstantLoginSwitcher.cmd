@echo off
setlocal EnableExtensions

cd /d "%~dp0"
set "LOG_FILE=%TEMP%\InstantLoginSwitcher-install.log"
set "CORE_SCRIPT=%CD%\scripts\Setup-InstantLoginSwitcher.ps1"
set "BOOTSTRAP_SCRIPT=%CD%\scripts\Invoke-SetupBootstrap.ps1"
set "ILS_BUILD=2026.02.20.7"
set "POWERSHELL_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
set "ILS_PRIMARY_USER=Samuel Wunderly"
set "ILS_SECONDARY_USER=Lizzy Wunderly"
set "ILS_MODE=Install"
set "EXIT_CODE=1"

call :WRITE_HEADER

echo InstantLoginSwitcher installer
echo.
echo Build: %ILS_BUILD%
echo Log file: %LOG_FILE%
echo.

fltmc >nul 2>&1
if errorlevel 1 goto NEED_ADMIN

if not exist "%CORE_SCRIPT%" goto MISSING_CORE
if not exist "%BOOTSTRAP_SCRIPT%" goto MISSING_BOOTSTRAP
if not exist "%POWERSHELL_EXE%" goto MISSING_POWERSHELL

echo Leave blank to keep defaults shown in [brackets].
echo.
set /p "PRIMARY_INPUT=Primary user [%ILS_PRIMARY_USER%]: "
if not "%PRIMARY_INPUT%"=="" set "ILS_PRIMARY_USER=%PRIMARY_INPUT%"
set /p "SECONDARY_INPUT=Secondary user [%ILS_SECONDARY_USER%]: "
if not "%SECONDARY_INPUT%"=="" set "ILS_SECONDARY_USER=%SECONDARY_INPUT%"

echo Primary user: %ILS_PRIMARY_USER% >> "%LOG_FILE%"
echo Secondary user: %ILS_SECONDARY_USER% >> "%LOG_FILE%"
echo Build: %ILS_BUILD% >> "%LOG_FILE%"
echo Core script: %CORE_SCRIPT% >> "%LOG_FILE%"
echo Bootstrap script: %BOOTSTRAP_SCRIPT% >> "%LOG_FILE%"
echo PowerShell: %POWERSHELL_EXE% >> "%LOG_FILE%"
echo Running in-memory PowerShell core... >> "%LOG_FILE%"

"%POWERSHELL_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $bootstrapPath = $env:BOOTSTRAP_SCRIPT; if (-not (Test-Path -LiteralPath $bootstrapPath)) { throw ('Bootstrap script not found: ' + $bootstrapPath) }; $bootstrapText = Get-Content -LiteralPath $bootstrapPath -Raw; & ([System.Management.Automation.ScriptBlock]::Create($bootstrapText))"
set "EXIT_CODE=%ERRORLEVEL%"
echo PowerShell exit code: %EXIT_CODE% >> "%LOG_FILE%"
echo.

if not "%EXIT_CODE%"=="0" goto INSTALL_FAILED

echo Install completed.
echo Sign out and back in once, then press Numpad4 + Numpad5 + Numpad6 together.
set "EXIT_CODE=0"
goto END

:NEED_ADMIN
echo This installer must be run as Administrator.
echo Please right-click this file and choose "Run as administrator".
echo ERROR: not running as admin >> "%LOG_FILE%"
goto END

:MISSING_CORE
echo Could not find core setup script:
echo %CORE_SCRIPT%
echo ERROR: missing core script >> "%LOG_FILE%"
goto END

:MISSING_BOOTSTRAP
echo Could not find bootstrap script:
echo %BOOTSTRAP_SCRIPT%
echo ERROR: missing bootstrap script >> "%LOG_FILE%"
goto END

:MISSING_POWERSHELL
echo Could not find Windows PowerShell executable:
echo %POWERSHELL_EXE%
echo ERROR: missing powershell executable >> "%LOG_FILE%"
goto END

:INSTALL_FAILED
echo Install failed with exit code %EXIT_CODE%.
goto END

:WRITE_HEADER
echo ============================================== > "%LOG_FILE%"
echo InstantLoginSwitcher install started %DATE% %TIME% >> "%LOG_FILE%"
echo Working directory: %CD% >> "%LOG_FILE%"
exit /b 0

:END
echo See log: %LOG_FILE%
echo.
pause
exit /b %EXIT_CODE%
