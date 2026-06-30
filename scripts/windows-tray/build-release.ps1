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
$helperProjectPath = Join-Path $repoRoot "apps\windows-context-helper\Rynat.WindowsContextHelper.csproj"
$helperProjectDir = Split-Path -Parent $helperProjectPath
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

function Convert-ToRegValue([string]$Value) {
    return $Value.Replace("\", "\\").Replace('"', '\"')
}

function Write-ContextMenuScripts([string]$PublishRoot) {
    $helperPath = Join-Path $PublishRoot "Rynat.WindowsContextHelper.exe"
    $escapedHelper = Convert-ToRegValue $helperPath
    $menuText = "复制 RYNAT 分享链接"

    $installPs1 = @"
`$ErrorActionPreference = "Stop"
`$helperPath = Join-Path `$PSScriptRoot "Rynat.WindowsContextHelper.exe"
if (-not (Test-Path `$helperPath)) {
    throw "Cannot find RYNAT context helper: `$helperPath"
}

`$entries = @(
    @{ Key = "Software\Classes\*\shell\RynatCopyLink"; Kind = "file" },
    @{ Key = "Software\Classes\Directory\shell\RynatCopyLink"; Kind = "directory" }
)

foreach (`$entry in `$entries) {
    `$key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey(`$entry.Key)
    `$key.SetValue("", "$menuText")
    `$key.SetValue("Icon", '"' + `$helperPath + '",0')
    `$key.Close()

    `$commandKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey(`$entry.Key + "\command")
    `$commandKey.SetValue("", '"' + `$helperPath + '" copy-link "%1" --kind ' + `$entry.Kind)
    `$commandKey.Close()
}

Write-Host "RYNAT Explorer context menu installed." -ForegroundColor Green
"@

    $uninstallPs1 = @"
`$ErrorActionPreference = "Stop"
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue -Path "HKCU:\Software\Classes\*\shell\RynatCopyLink"
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue -Path "HKCU:\Software\Classes\Directory\shell\RynatCopyLink"
Write-Host "RYNAT Explorer context menu removed." -ForegroundColor Green
"@

    $installBat = @"
@echo off
setlocal
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-context-menu.ps1"
if not "%ERRORLEVEL%"=="0" pause
"@

    $uninstallBat = @"
@echo off
setlocal
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall-context-menu.ps1"
if not "%ERRORLEVEL%"=="0" pause
"@

    $reg = @"
Windows Registry Editor Version 5.00

[HKEY_CURRENT_USER\Software\Classes\*\shell\RynatCopyLink]
@="$menuText"
"Icon"="\"$escapedHelper\",0"

[HKEY_CURRENT_USER\Software\Classes\*\shell\RynatCopyLink\command]
@="\"$escapedHelper\" copy-link \"%1\" --kind file"

[HKEY_CURRENT_USER\Software\Classes\Directory\shell\RynatCopyLink]
@="$menuText"
"Icon"="\"$escapedHelper\",0"

[HKEY_CURRENT_USER\Software\Classes\Directory\shell\RynatCopyLink\command]
@="\"$escapedHelper\" copy-link \"%1\" --kind directory"
"@

    Set-Content -Encoding UTF8 -Path (Join-Path $PublishRoot "install-context-menu.ps1") -Value $installPs1
    Set-Content -Encoding ASCII -Path (Join-Path $PublishRoot "install-context-menu.bat") -Value $installBat
    Set-Content -Encoding UTF8 -Path (Join-Path $PublishRoot "uninstall-context-menu.ps1") -Value $uninstallPs1
    Set-Content -Encoding ASCII -Path (Join-Path $PublishRoot "uninstall-context-menu.bat") -Value $uninstallBat
    Set-Content -Encoding Unicode -Path (Join-Path $PublishRoot "install-context-menu.reg") -Value $reg

    foreach ($scriptName in @("install-context-menu.ps1", "uninstall-context-menu.ps1")) {
        $scriptPath = Join-Path $PublishRoot $scriptName
        $tokens = $null
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$errors) | Out-Null
        if ($errors.Count -gt 0) {
            throw "Generated $scriptName has PowerShell syntax errors: $($errors[0].Message)"
        }
    }
}

Push-Location $repoRoot
try {
    Write-Host "Repository: $repoRoot" -ForegroundColor Cyan
    Write-Host "Publishing Windows tray WebView shell..." -ForegroundColor Cyan

    if (-not (Test-Path $projectPath)) {
        throw "Cannot find project: $projectPath"
    }

    if (-not (Test-Path $helperProjectPath)) {
        throw "Cannot find helper project: $helperProjectPath"
    }

    $baseOutputRoot = Get-OutputRoot $OutputDirectory
    if (-not $NoClean) {
        Remove-PathIfExists $baseOutputRoot
        Remove-PathIfExists (Join-Path $projectDir "bin")
        Remove-PathIfExists (Join-Path $helperProjectDir "bin")
        if (-not $SkipRestore) {
            Remove-PathIfExists (Join-Path $projectDir "obj")
            Remove-PathIfExists (Join-Path $helperProjectDir "obj")
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

    $helperPublishArgs = @(
        "publish",
        $helperProjectPath,
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
        $helperPublishArgs += @("-r", $RuntimeIdentifier)
    }

    if ($SkipRestore) {
        $helperPublishArgs += "--no-restore"
    }

    Invoke-NativeCommand -FilePath "dotnet" -Arguments $helperPublishArgs
    Write-ContextMenuScripts $publishRoot

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
