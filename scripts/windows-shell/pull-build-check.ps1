#requires -Version 7.0

param(
    [switch]$SkipPull,
    [switch]$Offline,
    [switch]$FullWorkspace,
    [switch]$KeepLocalChanges
)

$ErrorActionPreference = "Stop"
$scriptVersion = "2026-06-27.2"
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

function Get-PowerShellHost {
    $currentPwsh = Join-Path $PSHOME "pwsh.exe"
    if (Test-Path $currentPwsh) {
        return $currentPwsh
    }

    $pwsh = Get-Command "pwsh" -CommandType Application -ErrorAction SilentlyContinue
    if ($pwsh) {
        return $pwsh.Source
    }

    throw "PowerShell 7 (pwsh) is required. Install it with: winget install --id Microsoft.PowerShell --source winget"
}

function Clear-TrackedWorktreeChangesForPull {
    $unstaged = @(& git diff --name-only)
    if ($LASTEXITCODE -ne 0) {
        throw ("git diff failed with exit code {0}" -f $LASTEXITCODE)
    }

    $staged = @(& git diff --cached --name-only)
    if ($LASTEXITCODE -ne 0) {
        throw ("git diff --cached failed with exit code {0}" -f $LASTEXITCODE)
    }

    $changed = @($unstaged + $staged | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
    if ($changed.Count -eq 0) {
        return
    }

    if ($KeepLocalChanges) {
        Write-Host "Local tracked changes would block git pull:" -ForegroundColor Yellow
        foreach ($path in $changed) {
            Write-Host "  $path" -ForegroundColor Yellow
        }
        Write-Host ""
        Write-Host "Build the current checkout without pulling:" -ForegroundColor Yellow
        Write-Host "  scripts\windows-shell\pull-build-check.bat -SkipPull" -ForegroundColor Yellow
        Write-Host "Or allow the check script to discard tracked local changes by omitting -KeepLocalChanges." -ForegroundColor Yellow
        throw "git pull skipped because local tracked changes are present"
    }

    Write-Host "Discarding local tracked changes before git pull:" -ForegroundColor Yellow
    foreach ($path in $changed) {
        Write-Host "  $path" -ForegroundColor Yellow
    }

    & git restore --staged -- $changed
    if ($LASTEXITCODE -ne 0) {
        throw ("git restore --staged failed with exit code {0}" -f $LASTEXITCODE)
    }

    & git restore --worktree -- $changed
    if ($LASTEXITCODE -ne 0) {
        throw ("git restore --worktree failed with exit code {0}" -f $LASTEXITCODE)
    }
}

Push-Location $repoRoot
try {
    Write-Host "Repository: $repoRoot" -ForegroundColor Cyan
    Write-Host "Explorer-first build script: $scriptVersion" -ForegroundColor DarkGray

    if (-not $SkipPull) {
        Write-Host "Pulling latest changes..." -ForegroundColor Cyan
        Clear-TrackedWorktreeChangesForPull
        Invoke-NativeCommand git pull --ff-only
    }

    if (-not (Test-Path $buildCheckScript)) {
        throw "Cannot find build check script: $buildCheckScript"
    }

    $checkArgs = @()
    if ($Offline) {
        $checkArgs += "-Offline"
    }
    if ($FullWorkspace) {
        $checkArgs += "-FullWorkspace"
    }

    Write-Host "Running Explorer-first Windows shell checks..." -ForegroundColor Cyan
    & (Get-PowerShellHost) -NoProfile -ExecutionPolicy Bypass -File $buildCheckScript @checkArgs
    if ($LASTEXITCODE -ne 0) {
        throw "build-check.ps1 failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
