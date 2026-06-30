#requires -Version 7.0

param(
    [string]$PublishDirectory = "",
    [string]$OutputDirectory = ".\build\windows-installer"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$installerScript = Join-Path $PSScriptRoot "installer.iss"
$latestPath = Join-Path $repoRoot "build\windows-tray-release\latest.txt"

function Resolve-PublishDirectory {
    if (-not [string]::IsNullOrWhiteSpace($PublishDirectory)) {
        return (Resolve-Path $PublishDirectory).Path
    }

    if (-not (Test-Path $latestPath)) {
        throw "Cannot find latest release pointer: $latestPath. Run scripts\windows-tray\build-release.bat first."
    }

    $latest = (Get-Content -Raw -Path $latestPath).Trim()
    if ([string]::IsNullOrWhiteSpace($latest) -or -not (Test-Path $latest)) {
        throw "Latest release output does not exist: $latest"
    }

    return (Resolve-Path $latest).Path
}

function Find-InnoCompiler {
    $commands = @("iscc.exe", "iscc")
    foreach ($command in $commands) {
        $found = Get-Command $command -ErrorAction SilentlyContinue
        if ($found) {
            return $found.Source
        }
    }

    $commonPaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )

    foreach ($path in $commonPaths) {
        if (Test-Path $path) {
            return $path
        }
    }

    throw "Cannot find Inno Setup compiler. Install Inno Setup 6, then rerun this script."
}

$publishRoot = Resolve-PublishDirectory
$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
}

if (-not (Test-Path (Join-Path $publishRoot "Rynat.WindowsTray.exe"))) {
    throw "Publish directory does not contain Rynat.WindowsTray.exe: $publishRoot"
}

if (-not (Test-Path (Join-Path $publishRoot "Rynat.WindowsContextHelper.exe"))) {
    throw "Publish directory does not contain Rynat.WindowsContextHelper.exe: $publishRoot"
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$iscc = Find-InnoCompiler
$env:RYNAT_PUBLISH_DIR = $publishRoot

Write-Host "Building RYNAT Windows installer..." -ForegroundColor Cyan
Write-Host "Publish input: $publishRoot"
Write-Host "Installer output: $outputRoot"

& $iscc "/O$outputRoot" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE"
}

Write-Host "RYNAT Windows installer completed." -ForegroundColor Green
