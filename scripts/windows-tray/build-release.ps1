#requires -Version 7.0

param(
    [switch]$SkipRestore,
    [switch]$NoClean,
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDirectory = ".\build\windows-tray-release"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$projectPath = Join-Path $repoRoot "apps\windows-tray\Rynat.WindowsTray.csproj"

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [string[]]$Arguments = @()
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw ("{0} failed with exit code {1}: {0} {2}" -f $FilePath, $LASTEXITCODE, ($Arguments -join " "))
    }
}

function Get-OutputRoot([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Remove-PathIfExists([string]$Path) {
    if (Test-Path $Path) {
        Write-Host "Removing: $Path" -ForegroundColor DarkYellow
        Remove-Item -Recurse -Force -Path $Path
    }
}

Push-Location $repoRoot
try {
    Write-Host "Repository: $repoRoot" -ForegroundColor Cyan
    Write-Host "Publishing Windows tray WebView shell..." -ForegroundColor Cyan

    if (-not (Test-Path $projectPath)) {
        throw "Cannot find project: $projectPath"
    }

    $baseOutputRoot = Get-OutputRoot $OutputDirectory
    if (-not $NoClean) {
        Remove-PathIfExists $baseOutputRoot
    }

    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $publishRoot = Join-Path $baseOutputRoot $stamp

    $publishArgs = @(
        "publish",
        $projectPath,
        "-c",
        "Release",
        "-r",
        $RuntimeIdentifier,
        "--self-contained",
        "true",
        "-p:PublishSingleFile=false",
        "-o",
        $publishRoot
    )

    if ($SkipRestore) {
        $publishArgs += "--no-restore"
    }

    Invoke-NativeCommand -FilePath "dotnet" -Arguments $publishArgs

    $latestPath = Join-Path $baseOutputRoot "latest.txt"
    New-Item -ItemType Directory -Force -Path $baseOutputRoot | Out-Null
    Set-Content -Encoding UTF8 -Path $latestPath -Value $publishRoot

    Write-Host ""
    Write-Host "Windows tray WebView shell release completed." -ForegroundColor Green
    Write-Host "Output:"
    Write-Host "  $publishRoot"
    Write-Host "Latest pointer:"
    Write-Host "  $latestPath"
}
finally {
    Pop-Location
}
