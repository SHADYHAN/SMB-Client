#requires -Version 7.0

param(
    [switch]$SkipRestore
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

Push-Location $repoRoot
try {
    Write-Host "Repository: $repoRoot" -ForegroundColor Cyan
    Write-Host "Checking Windows tray WebView shell..." -ForegroundColor Cyan

    if (-not (Test-Path $projectPath)) {
        throw "Cannot find project: $projectPath"
    }

    if (-not $SkipRestore) {
        Invoke-NativeCommand -FilePath "dotnet" -Arguments @("restore", $projectPath)
    }

    Invoke-NativeCommand -FilePath "dotnet" -Arguments @(
        "build",
        $projectPath,
        "-c",
        "Debug",
        "--no-restore"
    )

    Write-Host "Windows tray WebView shell check completed." -ForegroundColor Green
}
finally {
    Pop-Location
}
