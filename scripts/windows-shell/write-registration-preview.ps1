#requires -Version 7.0

param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,

    [Parameter(Mandatory = $true)]
    [string]$HelperPath,

    [string]$OutputDirectory = ".\build\windows-shell-registration"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$protocolPath = Join-Path $OutputDirectory "rynat-protocol.reg"
$contextPath = Join-Path $OutputDirectory "rynat-context-menu.reg"

function ConvertTo-RegEscaped([string]$Value) {
    return $Value.Replace('\', '\\').Replace('"', '\"')
}

$exe = ConvertTo-RegEscaped $ExecutablePath
$helper = ConvertTo-RegEscaped $HelperPath

@"
Windows Registry Editor Version 5.00

[HKEY_CURRENT_USER\Software\Classes\rynat]
@="URL:RYNAT Link"
"URL Protocol"=""

[HKEY_CURRENT_USER\Software\Classes\rynat\DefaultIcon]
@="\"$exe\",0"

[HKEY_CURRENT_USER\Software\Classes\rynat\shell\open\command]
@="\"$exe\" \"%1\""
"@ | Set-Content -Encoding Unicode $protocolPath

@"
Windows Registry Editor Version 5.00

[HKEY_CURRENT_USER\Software\Classes\*\shell\RynatCopyLink]
@="复制 RYNAT 分享链接"
"Icon"="\"$helper\",0"

[HKEY_CURRENT_USER\Software\Classes\*\shell\RynatCopyLink\command]
@="\"$helper\" copy-link \"%1\" --kind file"

[HKEY_CURRENT_USER\Software\Classes\Directory\shell\RynatCopyLink]
@="复制 RYNAT 分享链接"
"Icon"="\"$helper\",0"

[HKEY_CURRENT_USER\Software\Classes\Directory\shell\RynatCopyLink\command]
@="\"$helper\" copy-link \"%1\" --kind directory"
"@ | Set-Content -Encoding Unicode $contextPath

Write-Host "Wrote:"
Write-Host "  $protocolPath"
Write-Host "  $contextPath"
Write-Host "Review these files before importing them into the registry."
