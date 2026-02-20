@echo off
setlocal EnableExtensions

cd /d "%~dp0"
set "LOG_FILE=%TEMP%\InstantLoginSwitcher-uninstall.log"
set "CORE_SCRIPT=%CD%\scripts\Setup-InstantLoginSwitcher.ps1"
set "BOOTSTRAP_SCRIPT=%CD%\scripts\Invoke-SetupBootstrap.ps1"
set "ILS_BUILD=2026.02.20.5"
set "POWERSHELL_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
set "ILS_MODE=Uninstall"
set "EXIT_CODE=1"

call :WRITE_HEADER

echo InstantLoginSwitcher uninstaller
echo.
echo Build: %ILS_BUILD%
echo Log file: %LOG_FILE%
echo.

fltmc >nul 2>&1
if errorlevel 1 goto NEED_ADMIN

if not exist "%CORE_SCRIPT%" goto MISSING_CORE
if not exist "%BOOTSTRAP_SCRIPT%" goto MISSING_BOOTSTRAP

echo Build: %ILS_BUILD% >> "%LOG_FILE%"
echo Core script: %CORE_SCRIPT% >> "%LOG_FILE%"
echo Bootstrap script: %BOOTSTRAP_SCRIPT% >> "%LOG_FILE%"
echo PowerShell: %POWERSHELL_EXE% >> "%LOG_FILE%"
echo Running in-memory PowerShell core... >> "%LOG_FILE%"

"%POWERSHELL_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $bootstrapPath = $env:BOOTSTRAP_SCRIPT; if (-not (Test-Path -LiteralPath $bootstrapPath)) { throw ('Bootstrap script not found: ' + $bootstrapPath) }; $bootstrapText = Get-Content -LiteralPath $bootstrapPath -Raw; & ([System.Management.Automation.ScriptBlock]::Create($bootstrapText))"
set "EXIT_CODE=%ERRORLEVEL%"
echo PowerShell exit code: %EXIT_CODE% >> "%LOG_FILE%"
echo.

if not "%EXIT_CODE%"=="0" goto UNINSTALL_FAILED

echo Uninstall completed.
set "EXIT_CODE=0"
goto END

:NEED_ADMIN
echo This uninstaller must be run as Administrator.
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

:UNINSTALL_FAILED
echo Uninstall failed with exit code %EXIT_CODE%.
goto END

:WRITE_HEADER
echo ============================================== > "%LOG_FILE%"
echo InstantLoginSwitcher uninstall started %DATE% %TIME% >> "%LOG_FILE%"
echo Working directory: %CD% >> "%LOG_FILE%"
exit /b 0

:END
echo See log: %LOG_FILE%
echo.
pause
exit /b %EXIT_CODE%
