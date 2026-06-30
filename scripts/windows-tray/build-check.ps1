#requires -Version 7.0

param(
    [switch]$SkipRestore,
    [switch]$NoClean
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$projectPath = Join-Path $repoRoot "apps\windows-tray\Rynat.WindowsTray.csproj"
$projectDir = Split-Path -Parent $projectPath
$helperProjectPath = Join-Path $repoRoot "apps\windows-context-helper\Rynat.WindowsContextHelper.csproj"
$helperProjectDir = Split-Path -Parent $helperProjectPath

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

function Invoke-NativeCommandBestEffort {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [string[]]$Arguments = @()
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        Write-Warning ("{0} exited with code {1}: {0} {2}" -f $FilePath, $LASTEXITCODE, ($Arguments -join " "))
    }
}

function Remove-PathIfExists([string]$Path) {
    if (Test-Path $Path) {
        Write-Host "Removing: $Path" -ForegroundColor DarkYellow
        Remove-Item -Recurse -Force -Path $Path
    }
}

Push-Location $repoRoot
try {
    Write-Host "Repository: $repoRoot" -ForegroundColor Cyan
    Write-Host "Checking RYNAT Windows client..." -ForegroundColor Cyan

    if (-not (Test-Path $projectPath)) {
        throw "Cannot find project: $projectPath"
    }

    if (-not (Test-Path $helperProjectPath)) {
        throw "Cannot find helper project: $helperProjectPath"
    }

    Write-Host "Stopping dotnet build servers..." -ForegroundColor DarkCyan
    Invoke-NativeCommandBestEffort -FilePath "dotnet" -Arguments @("build-server", "shutdown")

    if (-not $NoClean) {
        Remove-PathIfExists (Join-Path $projectDir "bin")
        Remove-PathIfExists (Join-Path $helperProjectDir "bin")
        if (-not $SkipRestore) {
            Remove-PathIfExists (Join-Path $projectDir "obj")
            Remove-PathIfExists (Join-Path $helperProjectDir "obj")
        }
    }

    if (-not $SkipRestore) {
        Invoke-NativeCommand -FilePath "dotnet" -Arguments @("restore", $projectPath)
        Invoke-NativeCommand -FilePath "dotnet" -Arguments @("restore", $helperProjectPath)
    }

    Invoke-NativeCommand -FilePath "dotnet" -Arguments @(
        "build",
        $projectPath,
        "-c",
        "Debug",
        "-p:UseSharedCompilation=false",
        "-p:BuildInParallel=false",
        "-p:RunAnalyzers=false",
        "-maxcpucount:1",
        "-nodeReuse:false",
        "--no-restore"
    )

    Invoke-NativeCommand -FilePath "dotnet" -Arguments @(
        "build",
        $helperProjectPath,
        "-c",
        "Debug",
        "-p:UseSharedCompilation=false",
        "-p:BuildInParallel=false",
        "-p:RunAnalyzers=false",
        "-maxcpucount:1",
        "-nodeReuse:false",
        "--no-restore"
    )

    Write-Host "RYNAT Windows client check completed." -ForegroundColor Green
}
finally {
    Pop-Location
}
