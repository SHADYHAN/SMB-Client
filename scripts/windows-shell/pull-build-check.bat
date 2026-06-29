@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%pull-build-check.ps1"

if not exist "%PS_SCRIPT%" (
    echo Cannot find PowerShell script: %PS_SCRIPT%
    pause
    exit /b 1
)

where pwsh >nul 2>nul
if "%ERRORLEVEL%"=="0" (
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
) else (
    echo PowerShell 7 is required for the Explorer-first Windows build scripts.
    echo Install it with:
    echo   winget install --id Microsoft.PowerShell --source winget
    echo.
    pause
    exit /b 1
)
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo.
    echo Explorer-first build check failed. Exit code: %EXIT_CODE%
    pause
    exit /b %EXIT_CODE%
)

echo.
echo Explorer-first build check completed.
pause
exit /b 0
