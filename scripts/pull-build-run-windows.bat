@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%pull-build-run-windows.ps1"

if not exist "%PS_SCRIPT%" (
    echo Cannot find PowerShell script: %PS_SCRIPT%
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo.
    echo Build or launch failed. Exit code: %EXIT_CODE%
    pause
    exit /b %EXIT_CODE%
)

echo.
echo Done.
pause
exit /b 0
