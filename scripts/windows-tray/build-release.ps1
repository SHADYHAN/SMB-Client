#requires -Version 7.0

param(
    [switch]$SkipRestore,
    [switch]$NoClean,
    [switch]$SelfContained,
    [string]$RuntimeIdentifier = "",
    [string]$OutputDirectory = ".\build\windows-tray-release"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$projectPath = Join-Path $repoRoot "apps\windows-tray\Rynat.WindowsTray.csproj"
$projectDir = Split-Path -Parent $projectPath
$script:LastNativeExitCode = 0

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [string[]]$Arguments = @()
    )

    & $FilePath @Arguments
    $script:LastNativeExitCode = $LASTEXITCODE
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

function Get-OutputRoot([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
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
    Write-Host "Publishing Windows tray WebView shell..." -ForegroundColor Cyan

    if (-not (Test-Path $projectPath)) {
        throw "Cannot find project: $projectPath"
    }

    $baseOutputRoot = Get-OutputRoot $OutputDirectory
    if (-not $NoClean) {
        Remove-PathIfExists $baseOutputRoot
        Remove-PathIfExists (Join-Path $projectDir "bin")
        if (-not $SkipRestore) {
            Remove-PathIfExists (Join-Path $projectDir "obj")
        }
    }

    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $publishRoot = Join-Path $baseOutputRoot $stamp
    $isSelfContained = if ($SelfContained) { "true" } else { "false" }

    Write-Host "Stopping dotnet build servers..." -ForegroundColor DarkCyan
    Invoke-NativeCommandBestEffort -FilePath "dotnet" -Arguments @("build-server", "shutdown")

    $publishArgs = @(
        "publish",
        $projectPath,
        "-c",
        "Release",
        "-p:PublishSingleFile=false",
        "-p:SelfContained=$isSelfContained",
        "-p:UseSharedCompilation=false",
        "-p:BuildInParallel=false",
        "-p:RunAnalyzers=false",
        "-maxcpucount:1",
        "-nodeReuse:false",
        "-o",
        $publishRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
        $publishArgs += @("-r", $RuntimeIdentifier)
    }

    if ($SkipRestore) {
        $publishArgs += "--no-restore"
    }

    try {
        Invoke-NativeCommand -FilePath "dotnet" -Arguments $publishArgs
    }
    catch {
        if ($script:LastNativeExitCode -ne -1073741819) {
            throw
        }

        Write-Warning "dotnet publish crashed with 0xC0000005. Retrying once after shutting down build servers and cleaning project outputs."
        Invoke-NativeCommandBestEffort -FilePath "dotnet" -Arguments @("build-server", "shutdown")
        Remove-PathIfExists (Join-Path $projectDir "bin")
        if (-not $SkipRestore) {
            Remove-PathIfExists (Join-Path $projectDir "obj")
        }
        Invoke-NativeCommand -FilePath "dotnet" -Arguments $publishArgs
    }

    $latestPath = Join-Path $baseOutputRoot "latest.txt"
    New-Item -ItemType Directory -Force -Path $baseOutputRoot | Out-Null
    Set-Content -Encoding UTF8 -Path $latestPath -Value $publishRoot

    Write-Host ""
    Write-Host "Windows tray WebView shell release completed." -ForegroundColor Green
    Write-Host "Output:"
    Write-Host "  $publishRoot"
    Write-Host "Latest pointer:"
    Write-Host "  $latestPath"
}
finally {
    Pop-Location
}
