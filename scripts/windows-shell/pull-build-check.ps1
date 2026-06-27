param(
    [switch]$SkipPull,
    [switch]$Offline
)

$ErrorActionPreference = "Stop"
$scriptVersion = "2026-06-27.1"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$buildCheckScript = Join-Path $PSScriptRoot "build-check.ps1"

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE"
    }
}

Push-Location $repoRoot
try {
    Write-Host "Repository: $repoRoot" -ForegroundColor Cyan
    Write-Host "Explorer-first build script: $scriptVersion" -ForegroundColor DarkGray

    if (-not $SkipPull) {
        Write-Host "Pulling latest changes..." -ForegroundColor Cyan
        Invoke-NativeCommand git pull --ff-only
    }

    if (-not (Test-Path $buildCheckScript)) {
        throw "Cannot find build check script: $buildCheckScript"
    }

    $checkArgs = @()
    if ($Offline) {
        $checkArgs += "-Offline"
    }

    Write-Host "Running Explorer-first Windows shell checks..." -ForegroundColor Cyan
    & powershell -NoProfile -ExecutionPolicy Bypass -File $buildCheckScript @checkArgs
    if ($LASTEXITCODE -ne 0) {
        throw "build-check.ps1 failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
