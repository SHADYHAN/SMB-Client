param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$cargoArguments = @("build", "-p", "rynat-core")

if ($Configuration -eq "Release") {
    $cargoArguments += "--release"
}

Write-Host "Building rynat-core for Windows ($Configuration)..."
& cargo @cargoArguments
if ($LASTEXITCODE -ne 0) {
    throw "cargo build failed with exit code $LASTEXITCODE"
}

$profileDir = if ($Configuration -eq "Release") { "release" } else { "debug" }
$dllPath = Join-Path $root "target\$profileDir\rynat_core.dll"
$pdbPath = Join-Path $root "target\$profileDir\rynat_core.pdb"

if (-not (Test-Path $dllPath)) {
    throw "Expected core library was not produced: $dllPath"
}

if (-not [string]::IsNullOrWhiteSpace($OutputDir)) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
    Copy-Item $dllPath (Join-Path $OutputDir "rynat_core.dll") -Force

    if (Test-Path $pdbPath) {
        Copy-Item $pdbPath (Join-Path $OutputDir "rynat_core.pdb") -Force
    }

    Write-Host "Copied rynat-core artifacts to $OutputDir"
}

Write-Host "rynat-core.dll ready at $dllPath"
