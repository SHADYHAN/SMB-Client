param(
    [switch]$SkipPull,
    [switch]$SkipChecks,
    [switch]$SkipNpmInstall,
    [switch]$Offline,
    [string]$OutputDirectory = ".\build\windows-shell-release"
)

$ErrorActionPreference = "Stop"
$scriptVersion = "2026-06-27.2"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$windowsShellDir = Join-Path $repoRoot "apps\windows-shell"
$buildCheckScript = Join-Path $PSScriptRoot "build-check.ps1"
$registrationScript = Join-Path $PSScriptRoot "write-registration-preview.ps1"

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [string[]]$Arguments = @()
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw ("{0} failed with exit code {1}: {0} {2}" -f $FilePath, $LASTEXITCODE, ($Arguments -join ' '))
    }
}

function Invoke-NpmInstallWithRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & npm @Arguments
    if ($LASTEXITCODE -eq 0) {
        return
    }

    $firstExitCode = $LASTEXITCODE
    Write-Warning ("npm install failed with exit code {0}. Cleaning npm cache and retrying once..." -f $firstExitCode)

    & npm cache clean --force
    if ($LASTEXITCODE -ne 0) {
        throw ("npm cache clean failed with exit code {0}" -f $LASTEXITCODE)
    }

    & npm @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw ("npm failed with exit code {0}: npm {1}" -f $LASTEXITCODE, ($Arguments -join ' '))
    }
}

function Get-OutputRoot([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Copy-IfExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,

        [Parameter(Mandatory = $true)]
        [string]$DestinationDirectory
    )

    if (-not (Test-Path $SourcePath)) {
        return $null
    }

    $destination = Join-Path $DestinationDirectory (Split-Path $SourcePath -Leaf)
    Copy-Item -Force -Path $SourcePath -Destination $destination
    return $destination
}

Push-Location $repoRoot
try {
    Write-Host "Repository: $repoRoot" -ForegroundColor Cyan
    Write-Host "Explorer-first release script: $scriptVersion" -ForegroundColor DarkGray

    if (-not $SkipPull) {
        Write-Host "Pulling latest changes..." -ForegroundColor Cyan
        Invoke-NativeCommand -FilePath "git" -Arguments @("pull", "--ff-only")
    }

    if (-not $SkipChecks) {
        if (-not (Test-Path $buildCheckScript)) {
            throw "Cannot find build check script: $buildCheckScript"
        }

        $checkArgs = @()
        if ($Offline) {
            $checkArgs += "-Offline"
        }

        Write-Host "Running Explorer-first checks before release..." -ForegroundColor Cyan
        Invoke-NativeCommand -FilePath "powershell" -Arguments (@("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $buildCheckScript) + $checkArgs)
    }

    Push-Location $windowsShellDir
    try {
        if (-not $SkipNpmInstall) {
            $npmInstallArgs = @("install", "--no-audit", "--no-fund")
            if ($Offline) {
                $npmInstallArgs += "--offline"
            }

            Write-Host "Installing Windows shell frontend dependencies..." -ForegroundColor Cyan
            Invoke-NpmInstallWithRetry -Arguments $npmInstallArgs
        }

        Write-Host "Building Tauri Windows shell bundle..." -ForegroundColor Cyan
        Invoke-NativeCommand -FilePath "npm" -Arguments @("run", "build")
    }
    finally {
        Pop-Location
    }

    $cargoArgs = @("build", "-p", "rynat-windows-context-helper", "--release", "--locked")
    if ($Offline) {
        $cargoArgs += "--offline"
    }

    Write-Host "Building Explorer context helper release exe..." -ForegroundColor Cyan
    Invoke-NativeCommand -FilePath "cargo" -Arguments $cargoArgs

    $baseOutputRoot = Get-OutputRoot $OutputDirectory
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $outputRoot = Join-Path $baseOutputRoot $stamp
    $installersDir = Join-Path $outputRoot "installers"
    $binDir = Join-Path $outputRoot "bin"
    $registrationDir = Join-Path $outputRoot "registration-preview"

    New-Item -ItemType Directory -Force -Path $installersDir | Out-Null
    New-Item -ItemType Directory -Force -Path $binDir | Out-Null

    $bundleDir = Join-Path $windowsShellDir "src-tauri\target\release\bundle"
    $copiedInstallers = @()
    if (Test-Path $bundleDir) {
        Get-ChildItem -Path $bundleDir -Recurse -File |
            Where-Object { $_.Extension -in @(".msi", ".exe") } |
            ForEach-Object {
                $destination = Join-Path $installersDir $_.Name
                Copy-Item -Force -Path $_.FullName -Destination $destination
                $copiedInstallers += $destination
            }
    }

    $helperExe = Join-Path $repoRoot "target\release\rynat-windows-context-helper.exe"
    $copiedHelper = Copy-IfExists -SourcePath $helperExe -DestinationDirectory $binDir

    $tauriReleaseDir = Join-Path $windowsShellDir "src-tauri\target\release"
    $appExeCandidates = @(
        (Join-Path $tauriReleaseDir "rynat-windows-shell.exe"),
        (Join-Path $tauriReleaseDir "RYNAT.exe")
    )
    $copiedApp = $null
    foreach ($candidate in $appExeCandidates) {
        $copiedApp = Copy-IfExists -SourcePath $candidate -DestinationDirectory $binDir
        if ($copiedApp) {
            break
        }
    }

    if ($copiedApp -and $copiedHelper -and (Test-Path $registrationScript)) {
        Write-Host "Writing local registration preview files..." -ForegroundColor Cyan
        Invoke-NativeCommand -FilePath "powershell" -Arguments @(
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            $registrationScript,
            "-ExecutablePath",
            $copiedApp,
            "-HelperPath",
            $copiedHelper,
            "-OutputDirectory",
            $registrationDir
        )
    }

    $latestPath = Join-Path $baseOutputRoot "latest.txt"
    New-Item -ItemType Directory -Force -Path $baseOutputRoot | Out-Null
    Set-Content -Encoding UTF8 -Path $latestPath -Value $outputRoot

    Write-Host ""
    Write-Host "Explorer-first release build completed." -ForegroundColor Green
    Write-Host "Output root:"
    Write-Host "  $outputRoot"
    Write-Host "Latest pointer:"
    Write-Host "  $latestPath"

    if ($copiedInstallers.Count -gt 0) {
        Write-Host "Installers:"
        foreach ($installer in $copiedInstallers) {
            Write-Host "  $installer"
        }
    } else {
        Write-Warning "No Tauri installer was copied. Check $bundleDir"
    }

    if ($copiedApp) {
        Write-Host "App exe:"
        Write-Host "  $copiedApp"
    }

    if ($copiedHelper) {
        Write-Host "Context helper exe:"
        Write-Host "  $copiedHelper"
    } else {
        Write-Warning "Context helper exe was not found at $helperExe"
    }

    if (Test-Path $registrationDir) {
        Write-Host "Registration preview:"
        Write-Host "  $registrationDir"
    }
}
finally {
    Pop-Location
}
