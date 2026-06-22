param(
    [string]$CoreLibraryPath
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$surfaceCheckScript = Join-Path $root "scripts\check-bridge-surface.ps1"
$project = Join-Path $root "tools\ffi-smoke-test\windows-dotnet\RynatFfiSmokeTest.csproj"
$nugetConfig = Join-Path $root "tools\ffi-smoke-test\windows-dotnet\NuGet.Config"
$projectOutputDir = Join-Path $root "tools\ffi-smoke-test\windows-dotnet\bin\Debug\net8.0"
$defaultDllCandidates = @(
    (Join-Path $root "target\debug\rynat_core.dll"),
    (Join-Path $root "target\release\rynat_core.dll")
)

if ([string]::IsNullOrWhiteSpace($CoreLibraryPath)) {
    $CoreLibraryPath = $defaultDllCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($CoreLibraryPath) -or -not (Test-Path $CoreLibraryPath)) {
    Write-Error @"
Could not find rynat_core.dll.

Expected one of:
 - $($defaultDllCandidates -join "`n - ")

Provide a path explicitly:
  powershell -ExecutionPolicy Bypass -File scripts\ffi-smoke-test-windows.ps1 -CoreLibraryPath C:\path\to\rynat_core.dll
"@
}

Write-Host "Checking Rust/header/Swift/C# bridge surface..." -ForegroundColor Cyan
powershell -ExecutionPolicy Bypass -File $surfaceCheckScript

dotnet build $project --configuration Debug --configfile $nugetConfig

New-Item -ItemType Directory -Force -Path $projectOutputDir | Out-Null
Copy-Item $CoreLibraryPath (Join-Path $projectOutputDir "rynat_core.dll") -Force

dotnet run --project $project --configuration Debug --no-build --no-launch-profile --configfile $nugetConfig
