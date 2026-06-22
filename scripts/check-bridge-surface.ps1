$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$bridgeRs = Join-Path $root "crates\rynat-core\src\bridge.rs"
$header = Join-Path $root "include\rynat_core.h"
$swiftBridge = Join-Path $root "apps\macos\RYNATClient\RynatCore.swift"
$csBridge = Join-Path $root "apps\windows\RynatCoreBridge.cs"

function Get-UniqueSorted([string[]]$values) {
    return $values | Where-Object { $_ } | Sort-Object -Unique
}

function Filter-BridgeSurface([string[]]$values) {
    return Get-UniqueSorted ($values | Where-Object { $_ -ne "rynat_free_string" })
}

function Extract-RustExports {
    $pattern = 'pub extern "C" fn (rynat_[a-z0-9_]+)'
    return Filter-BridgeSurface ((Get-Content $bridgeRs) | ForEach-Object {
        if ($_ -match $pattern) { $matches[1] }
    })
}

function Extract-HeaderExports {
    $pattern = '^(?:char \*|void )\s*(rynat_[a-z0-9_]+)\('
    return Filter-BridgeSurface ((Get-Content $header) | ForEach-Object {
        if ($_ -match $pattern) { $matches[1] }
    })
}

function Extract-SwiftExports {
    $pattern = '@_silgen_name\("(rynat_[a-z0-9_]+)"\)'
    return Filter-BridgeSurface ((Get-Content $swiftBridge) | ForEach-Object {
        if ($_ -match $pattern) { $matches[1] }
    })
}

function Extract-CSharpExports {
    $pattern = 'internal static extern (?:IntPtr|void)\s+(rynat_[a-z0-9_]+)\('
    return Filter-BridgeSurface ((Get-Content $csBridge) | ForEach-Object {
        if ($_ -match $pattern) { $matches[1] }
    })
}

function Compare-Surfaces([string]$name, [string[]]$expected, [string[]]$actual) {
    $missing = @($expected | Where-Object { $_ -notin $actual })
    $extra = @($actual | Where-Object { $_ -notin $expected })

    if ($missing.Count -eq 0 -and $extra.Count -eq 0) {
        Write-Host "${name}: OK ($($actual.Count) symbols)"
        return $true
    }

    Write-Host "${name}: mismatch" -ForegroundColor Red
    if ($missing.Count -gt 0) {
        Write-Host "  Missing:"
        $missing | ForEach-Object { Write-Host "    $_" }
    }
    if ($extra.Count -gt 0) {
        Write-Host "  Extra:"
        $extra | ForEach-Object { Write-Host "    $_" }
    }
    return $false
}

$rust = Extract-RustExports
$headerExports = Extract-HeaderExports
$swift = Extract-SwiftExports
$csharp = Extract-CSharpExports

$ok = $true
$ok = (Compare-Surfaces "Header vs Rust" $rust $headerExports) -and $ok
$ok = (Compare-Surfaces "Swift bridge vs Rust" $rust $swift) -and $ok
$ok = (Compare-Surfaces "C# bridge vs Rust" $rust $csharp) -and $ok

if (-not $ok) {
    exit 1
}

Write-Host "Bridge surface check passed." -ForegroundColor Green
