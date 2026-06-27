param(
    [switch]$Offline
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot

$cargoArgs = @()
if ($Offline) {
    $cargoArgs += "--offline"
}

Write-Host "Checking Explorer-first support workspace..."
cargo test --workspace --locked @cargoArgs

Write-Host "Checking Tauri shell Rust side..."
cargo test --manifest-path apps/windows-shell/src-tauri/Cargo.toml @cargoArgs

Write-Host "Checking helper contract..."
cargo run -p rynat-windows-context-helper --locked @cargoArgs -- copy-link "\\nas.local\Media\demo.mp4" --kind file

Write-Host "Explorer-first Windows shell checks completed."
