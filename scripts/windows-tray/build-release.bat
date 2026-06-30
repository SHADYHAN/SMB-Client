@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%build-release.ps1"

if not exist "%PS_SCRIPT%" (
    echo Cannot find PowerShell script: %PS_SCRIPT%
    pause
    exit /b 1
)

where pwsh >nul 2>nul
if not "%ERRORLEVEL%"=="0" (
    echo PowerShell 7 is required for the Windows tray build scripts.
    echo Install it with:
    echo   winget install --id Microsoft.PowerShell --source winget
    echo.
    pause
    exit /b 1
)

pwsh -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo.
    echo Windows tray release build failed. Exit code: %EXIT_CODE%
    pause
    exit /b %EXIT_CODE%
)

echo.
echo Windows tray release build completed.
pause
exit /b 0
