param(
    [switch]$Offline
)

$ErrorActionPreference = "Stop"
$scriptVersion = "2026-06-27.2"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot

$cargoArgs = @()
if ($Offline) {
    $cargoArgs += "--offline"
}

function Invoke-CargoChecked {
    param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    & cargo @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw ("cargo failed with exit code {0}: cargo {1}" -f $LASTEXITCODE, ($Arguments -join ' '))
    }
}

Write-Host "Explorer-first check script: $scriptVersion" -ForegroundColor DarkGray

Write-Host "Checking Explorer-first support workspace..."
Invoke-CargoChecked test --workspace --locked @cargoArgs

Write-Host "Checking Tauri shell Rust side..."
Invoke-CargoChecked test --manifest-path apps/windows-shell/src-tauri/Cargo.toml @cargoArgs

Write-Host "Checking helper contract..."
Invoke-CargoChecked run -p rynat-windows-context-helper --locked @cargoArgs -- copy-link "\\nas.local\Media\demo.mp4" --kind file

Write-Host "Explorer-first Windows shell checks completed."
