param(
    [switch]$Offline,
    [switch]$FullWorkspace,
    [string]$OpenExplorerPath
)

$ErrorActionPreference = "Stop"
$scriptVersion = "2026-06-27.6"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$tauriTargetRoot = Join-Path $repoRoot "apps\windows-shell\src-tauri\target"
Set-Location $repoRoot

$cargoArgs = @()
if ($Offline) {
    $cargoArgs += "--offline"
}

function Invoke-CargoChecked {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & cargo @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw ("cargo failed with exit code {0}: cargo {1}" -f $LASTEXITCODE, ($Arguments -join ' '))
    }
}

function Clear-TauriDebugSymbols {
    if (-not (Test-Path $tauriTargetRoot)) {
        return
    }

    $staleSymbols = Get-ChildItem -Path $tauriTargetRoot -Recurse -File -Include *.pdb,*.ilk -ErrorAction SilentlyContinue
    if (-not $staleSymbols) {
        return
    }

    Write-Warning ("Clearing {0} stale Tauri debug symbol file(s) before retry..." -f $staleSymbols.Count)
    $staleSymbols | Remove-Item -Force
}

function Invoke-CargoCheckedWithTauriPdbRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & cargo @Arguments
    if ($LASTEXITCODE -eq 0) {
        return
    }

    $firstExitCode = $LASTEXITCODE
    Write-Warning ("cargo failed with exit code {0}. Cleaning Tauri PDB files and retrying once..." -f $firstExitCode)
    Clear-TauriDebugSymbols

    & cargo @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw ("cargo failed with exit code {0}: cargo {1}" -f $LASTEXITCODE, ($Arguments -join ' '))
    }
}

Write-Host "Explorer-first check script: $scriptVersion" -ForegroundColor DarkGray

if ($FullWorkspace) {
    Write-Host "Checking full Rust workspace..." -ForegroundColor Cyan
    Invoke-CargoChecked -Arguments (@("test", "--workspace", "--locked") + $cargoArgs)
} else {
    Write-Host "Checking Explorer-first support crate..." -ForegroundColor Cyan
    Invoke-CargoChecked -Arguments (@("test", "-p", "rynat-windows-shell-support", "--locked") + $cargoArgs)

    Write-Host "Checking Explorer-first context helper..." -ForegroundColor Cyan
    Invoke-CargoChecked -Arguments (@("test", "-p", "rynat-windows-context-helper", "--locked") + $cargoArgs)
}

Write-Host "Checking Tauri shell Rust side..." -ForegroundColor Cyan
Invoke-CargoCheckedWithTauriPdbRetry -Arguments (@("test", "--manifest-path", "apps/windows-shell/src-tauri/Cargo.toml") + $cargoArgs)

Write-Host "Checking helper contract..." -ForegroundColor Cyan
Invoke-CargoChecked -Arguments (@("run", "-p", "rynat-windows-context-helper", "--locked") + $cargoArgs + @("--", "--print-only", "copy-link", "\\192.168.102.136\临时文件夹\123", "--kind", "dir"))

if (-not [string]::IsNullOrWhiteSpace($OpenExplorerPath)) {
    Write-Host "Checking Explorer open request..." -ForegroundColor Cyan
    Invoke-CargoChecked -Arguments (@("run", "-p", "rynat-windows-context-helper", "--bin", "open-explorer-check", "--locked") + $cargoArgs + @("--", $OpenExplorerPath))
}

Write-Host "Explorer-first Windows shell checks completed."
