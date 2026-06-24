param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$SkipPull,
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "apps\windows\Rynat.WindowsClient.csproj"

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

function Stop-RunningClient {
    $processNames = @("Rynat.WindowsClient", "RYNATClient")
    foreach ($name in $processNames) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force
    }
}

Push-Location $root
try {
    Write-Host "Repository: $root" -ForegroundColor Cyan

    if (-not $SkipPull) {
        Write-Host "Pulling latest changes..." -ForegroundColor Cyan
        Invoke-NativeCommand git pull --ff-only
    }

    Write-Host "Building Windows client ($Configuration)..." -ForegroundColor Cyan
    Invoke-NativeCommand -FilePath dotnet -Arguments @(
        "build",
        $project,
        "--configuration",
        $Configuration,
        "--verbosity:minimal",
        "/nr:false"
    )

    $targetFramework = "net8.0-windows"
    $runtimeIdentifier = "win-x64"
    $exePath = Join-Path $root "apps\windows\bin\$Configuration\$targetFramework\$runtimeIdentifier\Rynat.WindowsClient.exe"
    if (-not (Test-Path $exePath)) {
        $fallback = Get-ChildItem -Path (Join-Path $root "apps\windows\bin\$Configuration") -Filter "Rynat.WindowsClient.exe" -Recurse -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if ($null -ne $fallback) {
            $exePath = $fallback.FullName
        }
    }

    if (-not (Test-Path $exePath)) {
        throw "Could not find built executable under apps\windows\bin\$Configuration"
    }

    Write-Host "Built: $exePath" -ForegroundColor Green

    if (-not $NoLaunch) {
        Write-Host "Starting Windows client..." -ForegroundColor Cyan
        Stop-RunningClient
        Start-Process -FilePath $exePath -WorkingDirectory (Split-Path -Parent $exePath)
    }
}
finally {
    Pop-Location
}
