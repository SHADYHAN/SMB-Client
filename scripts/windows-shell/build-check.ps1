param(
    [switch]$Offline,
    [switch]$FullWorkspace
)

$ErrorActionPreference = "Stop"
$scriptVersion = "2026-06-27.4"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
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
Invoke-CargoChecked -Arguments (@("test", "--manifest-path", "apps/windows-shell/src-tauri/Cargo.toml") + $cargoArgs)

Write-Host "Checking helper contract..." -ForegroundColor Cyan
Invoke-CargoChecked -Arguments (@("run", "-p", "rynat-windows-context-helper", "--locked") + $cargoArgs + @("--", "copy-link", "\\nas.local\Media\demo.mp4", "--kind", "file"))

Write-Host "Explorer-first Windows shell checks completed."
