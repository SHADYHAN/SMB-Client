param(
    [switch]$SkipCompileChecks
)

$ErrorActionPreference = "Continue"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$windowsShellManifest = Join-Path $repoRoot "apps\windows-shell\src-tauri\Cargo.toml"

function Write-Section([string]$Title) {
    Write-Host ""
    Write-Host "=== $Title ===" -ForegroundColor Cyan
}

function Invoke-DiagnosticCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [string[]]$Arguments = @()
    )

    Write-Host ""
    Write-Host ("> {0} {1}" -f $FilePath, ($Arguments -join " ")) -ForegroundColor DarkGray
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        Write-Warning ("{0} exited with code {1}" -f $FilePath, $LASTEXITCODE)
    }
}

Push-Location $repoRoot
try {
    Write-Host "Repository: $repoRoot" -ForegroundColor Cyan

    Write-Section "Rust"
    Invoke-DiagnosticCommand -FilePath "rustc" -Arguments @("-Vv")
    Invoke-DiagnosticCommand -FilePath "cargo" -Arguments @("-V")
    Invoke-DiagnosticCommand -FilePath "rustup" -Arguments @("show")
    Invoke-DiagnosticCommand -FilePath "rustup" -Arguments @("target", "list", "--installed")

    Write-Section "MSVC Toolchain"
    Invoke-DiagnosticCommand -FilePath "where.exe" -Arguments @("cl")
    Invoke-DiagnosticCommand -FilePath "where.exe" -Arguments @("link")
    Invoke-DiagnosticCommand -FilePath "where.exe" -Arguments @("rc")

    Write-Section "Node And Git"
    Invoke-DiagnosticCommand -FilePath "node" -Arguments @("-v")
    Invoke-DiagnosticCommand -FilePath "npm.cmd" -Arguments @("-v")
    Invoke-DiagnosticCommand -FilePath "git" -Arguments @("--version")

    Write-Section "Cargo Config"
    Invoke-DiagnosticCommand -FilePath "cargo" -Arguments @("metadata", "--no-deps", "--format-version", "1")

    if (-not $SkipCompileChecks) {
        Write-Section "Compile Checks"
        Invoke-DiagnosticCommand -FilePath "cargo" -Arguments @("check", "-p", "rynat-windows-shell-support", "--locked")
        Invoke-DiagnosticCommand -FilePath "cargo" -Arguments @("check", "-p", "rynat-windows-context-helper", "--locked")
        Invoke-DiagnosticCommand -FilePath "cargo" -Arguments @("check", "--manifest-path", $windowsShellManifest)
    }
}
finally {
    Pop-Location
}
