param(
    [switch]$SkipPull,
    [switch]$SkipChecks,
    [switch]$SkipNpmInstall,
    [switch]$Offline,
    [switch]$NoClean,
    [switch]$CleanNodeModules,
    [switch]$KeepLocalChanges,
    [switch]$AllowLiteFallback,
    [string]$OutputDirectory = ".\build\windows-shell-release"
)

$ErrorActionPreference = "Stop"
$scriptVersion = "2026-06-27.10"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$windowsShellDir = Join-Path $repoRoot "apps\windows-shell"
$windowsShellDistDir = Join-Path $windowsShellDir "dist"
$windowsShellTargetDir = Join-Path $windowsShellDir "src-tauri\target"
$windowsShellNodeModulesDir = Join-Path $windowsShellDir "node_modules"
$workspaceTargetDir = Join-Path $repoRoot "target"
$buildCheckScript = Join-Path $PSScriptRoot "build-check.ps1"
$registrationScript = Join-Path $PSScriptRoot "write-registration-preview.ps1"
$script:tauriBuildProfile = "release"
$script:useLiteShell = $false

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

    & npm.cmd @Arguments
    if ($LASTEXITCODE -eq 0) {
        return
    }

    $firstExitCode = $LASTEXITCODE
    Write-Warning ("npm install failed with exit code {0}. Cleaning npm cache and retrying once..." -f $firstExitCode)

    & npm.cmd cache clean --force
    if ($LASTEXITCODE -ne 0) {
        throw ("npm cache clean failed with exit code {0}" -f $LASTEXITCODE)
    }

    & npm.cmd @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw ("npm failed with exit code {0}: npm {1}" -f $LASTEXITCODE, ($Arguments -join ' '))
    }
}

function Write-RustcCrashHints {
    Write-Warning "Rust compiler process crashed. If the log contains STATUS_ACCESS_VIOLATION / 0xc0000005, check the Windows Rust/MSVC environment:"
    Write-Warning "  rustc -Vv"
    Write-Warning "  rustup show"
    Write-Warning "  rustup target list --installed"
    Write-Warning "  where.exe cl"
    Write-Warning "  where.exe link"
    Write-Warning "  where.exe rc"
    Write-Warning "Then retry after cleaning: scripts\windows-shell\build-release.bat -SkipPull"
}

function Invoke-NpmBuildWithRetry {
    $previousCargoBuildJobs = $env:CARGO_BUILD_JOBS
    $previousCargoIncremental = $env:CARGO_INCREMENTAL
    $previousCargoProfileReleaseOptLevel = $env:CARGO_PROFILE_RELEASE_OPT_LEVEL
    $env:CARGO_BUILD_JOBS = "1"
    $env:CARGO_INCREMENTAL = "0"
    $env:CARGO_PROFILE_RELEASE_OPT_LEVEL = "0"

    try {
        Write-Host "Using stable Cargo release settings: CARGO_BUILD_JOBS=1, CARGO_INCREMENTAL=0, CARGO_PROFILE_RELEASE_OPT_LEVEL=0" -ForegroundColor DarkGray
        & npm.cmd run build
        if ($LASTEXITCODE -eq 0) {
            $script:tauriBuildProfile = "release"
            return
        }

        $firstExitCode = $LASTEXITCODE
        Write-Warning ("tauri release build failed with exit code {0}. Cleaning Tauri target and retrying once as debug bundle..." -f $firstExitCode)
        Remove-PathIfExists -Path $windowsShellTargetDir

        & npm.cmd run build:debug
        if ($LASTEXITCODE -ne 0) {
            if ($AllowLiteFallback) {
                Write-Warning ("tauri debug build failed with exit code {0}. Falling back to native lite shell because -AllowLiteFallback was set." -f $LASTEXITCODE)
                $script:useLiteShell = $true
                return
            }

            Write-RustcCrashHints
            throw ("npm failed with exit code {0}: npm run build:debug" -f $LASTEXITCODE)
        }

        $script:tauriBuildProfile = "debug"
    }
    finally {
        if ($null -eq $previousCargoBuildJobs) {
            Remove-Item Env:CARGO_BUILD_JOBS -ErrorAction SilentlyContinue
        } else {
            $env:CARGO_BUILD_JOBS = $previousCargoBuildJobs
        }

        if ($null -eq $previousCargoIncremental) {
            Remove-Item Env:CARGO_INCREMENTAL -ErrorAction SilentlyContinue
        } else {
            $env:CARGO_INCREMENTAL = $previousCargoIncremental
        }

        if ($null -eq $previousCargoProfileReleaseOptLevel) {
            Remove-Item Env:CARGO_PROFILE_RELEASE_OPT_LEVEL -ErrorAction SilentlyContinue
        } else {
            $env:CARGO_PROFILE_RELEASE_OPT_LEVEL = $previousCargoProfileReleaseOptLevel
        }
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

function Remove-PathIfExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path $Path)) {
        return
    }

    Write-Host "Removing: $Path" -ForegroundColor DarkYellow
    Remove-Item -Recurse -Force -Path $Path
}

function Clear-TrackedWorktreeChangesForPull {
    $unstaged = @(& git diff --name-only)
    if ($LASTEXITCODE -ne 0) {
        throw ("git diff failed with exit code {0}" -f $LASTEXITCODE)
    }

    $staged = @(& git diff --cached --name-only)
    if ($LASTEXITCODE -ne 0) {
        throw ("git diff --cached failed with exit code {0}" -f $LASTEXITCODE)
    }

    $changed = @($unstaged + $staged | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
    if ($changed.Count -eq 0) {
        return
    }

    if ($KeepLocalChanges) {
        Write-Host "Local tracked changes would block git pull:" -ForegroundColor Yellow
        foreach ($path in $changed) {
            Write-Host "  $path" -ForegroundColor Yellow
        }
        Write-Host ""
        Write-Host "Build the current checkout without pulling:" -ForegroundColor Yellow
        Write-Host "  scripts\windows-shell\build-release.bat -SkipPull" -ForegroundColor Yellow
        Write-Host "Or allow the release script to discard tracked local changes by omitting -KeepLocalChanges." -ForegroundColor Yellow
        throw "git pull skipped because local tracked changes are present"
    }

    Write-Host "Discarding local tracked changes before git pull:" -ForegroundColor Yellow
    foreach ($path in $changed) {
        Write-Host "  $path" -ForegroundColor Yellow
    }

    & git restore --staged -- $changed
    if ($LASTEXITCODE -ne 0) {
        throw ("git restore --staged failed with exit code {0}" -f $LASTEXITCODE)
    }

    & git restore --worktree -- $changed
    if ($LASTEXITCODE -ne 0) {
        throw ("git restore --worktree failed with exit code {0}" -f $LASTEXITCODE)
    }
}

Push-Location $repoRoot
try {
    Write-Host "Repository: $repoRoot" -ForegroundColor Cyan
    Write-Host "Explorer-first release script: $scriptVersion" -ForegroundColor DarkGray

    $baseOutputRoot = Get-OutputRoot $OutputDirectory

    if (-not $NoClean) {
        Write-Host "Cleaning previous Explorer-first build outputs..." -ForegroundColor Cyan
        Remove-PathIfExists -Path $windowsShellDistDir
        Remove-PathIfExists -Path $windowsShellTargetDir
        Remove-PathIfExists -Path $workspaceTargetDir
        Remove-PathIfExists -Path $baseOutputRoot
    }

    if ($CleanNodeModules) {
        Write-Host "Cleaning Windows shell node_modules..." -ForegroundColor Cyan
        Remove-PathIfExists -Path $windowsShellNodeModulesDir
    }

    if (-not $SkipPull) {
        Write-Host "Pulling latest changes..." -ForegroundColor Cyan
        Clear-TrackedWorktreeChangesForPull
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
        Invoke-NpmBuildWithRetry
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

    if ($script:useLiteShell) {
        $liteCargoArgs = @("build", "-p", "rynat-windows-shell-lite", "--release", "--locked")
        if ($Offline) {
            $liteCargoArgs += "--offline"
        }

        Write-Host "Building native lite Windows shell release exe..." -ForegroundColor Cyan
        Invoke-NativeCommand -FilePath "cargo" -Arguments $liteCargoArgs
    }

    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $outputRoot = Join-Path $baseOutputRoot $stamp
    $installersDir = Join-Path $outputRoot "installers"
    $binDir = Join-Path $outputRoot "bin"
    $registrationDir = Join-Path $outputRoot "registration-preview"

    New-Item -ItemType Directory -Force -Path $installersDir | Out-Null
    New-Item -ItemType Directory -Force -Path $binDir | Out-Null

    $bundleDir = Join-Path $windowsShellDir "src-tauri\target\$script:tauriBuildProfile\bundle"
    $copiedInstallers = @()
    if ((-not $script:useLiteShell) -and (Test-Path $bundleDir)) {
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

    if ($script:useLiteShell) {
        $appExeCandidates = @(
            (Join-Path $repoRoot "target\release\rynat-windows-shell-lite.exe")
        )
    } else {
        $tauriReleaseDir = Join-Path $windowsShellDir "src-tauri\target\$script:tauriBuildProfile"
        $appExeCandidates = @(
            (Join-Path $tauriReleaseDir "rynat-windows-shell-app.exe"),
            (Join-Path $tauriReleaseDir "rynat-windows-shell.exe"),
            (Join-Path $tauriReleaseDir "RYNAT.exe")
        )
    }
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
    if ($script:useLiteShell) {
        Write-Host "Build mode:"
        Write-Host "  Native lite shell fallback"
    }
    Write-Host "Output root:"
    Write-Host "  $outputRoot"
    Write-Host "Latest pointer:"
    Write-Host "  $latestPath"

    if ($copiedInstallers.Count -gt 0) {
        Write-Host "Installers:"
        foreach ($installer in $copiedInstallers) {
            Write-Host "  $installer"
        }
    } elseif ($script:useLiteShell) {
        Write-Host "Installers:"
        Write-Host "  Not produced in native lite shell fallback mode."
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
