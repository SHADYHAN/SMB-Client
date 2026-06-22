param(
    [string]$BuildOutputDir
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$surfaceCheckScript = Join-Path $root "scripts\check-bridge-surface.ps1"
$windowsProject = Join-Path $root "apps\windows\Rynat.WindowsClient.csproj"
$windowsAppContext = Join-Path $root "apps\windows\AppServices\WindowsAppContext.cs"
$windowsApp = Join-Path $root "apps\windows\App.xaml.cs"
$fileDownloadService = Join-Path $root "apps\windows\AppServices\Files\FileDownloadService.cs"
$fileWriteService = Join-Path $root "apps\windows\AppServices\Files\FileWriteService.cs"
$stableCacheKey = Join-Path $root "apps\windows\Infrastructure\StableCacheKey.cs"
$folderUploadService = Join-Path $root "apps\windows\AppServices\Files\FileFolderUploadService.cs"
$dragDownloadPreparationService = Join-Path $root "apps\windows\AppServices\Files\FileDragDownloadPreparationService.cs"
$mainShellViewModel = Join-Path $root "apps\windows\UI\Main\MainShellViewModel.cs"
$mainWindow = Join-Path $root "apps\windows\MainWindow.xaml.cs"
$redirectServer = Join-Path $root "apps\windows\PlatformIntegration\Links\WindowsLocalRedirectServer.cs"
$singleInstanceManager = Join-Path $root "apps\windows\PlatformIntegration\Links\WindowsSingleInstanceManager.cs"
$cacheManagementService = Join-Path $root "apps\windows\AppServices\Cache\WindowsCacheManagementService.cs"

if ([string]::IsNullOrWhiteSpace($BuildOutputDir)) {
    $BuildOutputDir = Join-Path $root "apps\windows\bin\AppServiceSmoke"
}

function Assert-FileContains {
    param(
        [string]$Path,
        [string]$Pattern,
        [string]$Description
    )

    if (-not (Test-Path $Path)) {
        throw "Missing file: $Path"
    }

    $content = Get-Content -Raw -Encoding UTF8 $Path
    if ($content -notmatch $Pattern) {
        throw "Check failed: $Description"
    }

    Write-Host "OK: $Description"
}

function Assert-NativeCommandSucceeded {
    param(
        [string]$Description
    )

    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE"
    }
}

Write-Host "Checking bridge/header/Swift/C# ABI surface..." -ForegroundColor Cyan
powershell -ExecutionPolicy Bypass -File $surfaceCheckScript
Assert-NativeCommandSucceeded "Bridge surface check"

Write-Host "Checking Windows app service files..." -ForegroundColor Cyan

$requiredFiles = @(
    "apps\windows\AppServices\Tasks\WindowsFileTaskService.cs",
    "apps\windows\AppServices\Tasks\WindowsFileTaskHandle.cs",
    "apps\windows\AppServices\Tasks\WindowsFileTaskSnapshot.cs",
    "apps\windows\AppServices\Files\FileFolderUploadService.cs",
    "apps\windows\AppServices\Files\FileBatchOperationService.cs",
    "apps\windows\AppServices\Files\FileDragDownloadPreparationService.cs",
    "apps\windows\AppServices\Files\FileDragDownloadPreparationResult.cs",
    "apps\windows\AppServices\Cache\WindowsCacheManagementService.cs"
)

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $root $relativePath
    if (-not (Test-Path $path)) {
        throw "Missing Windows app service file: $relativePath"
    }
    Write-Host "OK: $relativePath"
}

Assert-FileContains $windowsAppContext "WindowsFileTaskService" "task service registered in WindowsAppContext"
Assert-FileContains $windowsAppContext "WindowsCacheManagementService" "cache management service registered in WindowsAppContext"
Assert-FileContains $windowsAppContext "FileFolderUploadService" "folder upload service registered in WindowsAppContext"
Assert-FileContains $windowsAppContext "FileBatchOperationService" "batch file operation service registered in WindowsAppContext"
Assert-FileContains $windowsAppContext "FileDragDownloadPreparationService" "drag download preparation service registered in WindowsAppContext"

Assert-FileContains -Path $fileDownloadService -Pattern "TryReuseDragCache" -Description "drag download supports local cache reuse"
Assert-FileContains -Path $fileDownloadService -Pattern "CoreOperationId" -Description "file download passes operation id to core"
Assert-FileContains -Path $fileDownloadService -Pattern "\.part" -Description "download uses .part file to avoid partial cache reuse"
Assert-FileContains -Path $stableCacheKey -Pattern "SHA256" -Description "Windows cache keys use stable hashing"
Assert-FileContains -Path $folderUploadService -Pattern "UploadDirectoryRecursive" -Description "folder upload supports recursive upload"
Assert-FileContains -Path $fileWriteService -Pattern "ListRemoteItemsByName" -Description "paste folder copy checks existing nested target items"
Assert-FileContains -Path $fileWriteService -Pattern "replaceExisting" -Description "paste folder copy can replace nested file conflicts"
Assert-FileContains -Path $dragDownloadPreparationService -Pattern "PrepareManyAsync" -Description "drag download supports multi-select preparation"
Assert-FileContains -Path $mainShellViewModel -Pattern "PrepareItemsForDragDownload" -Description "view model exposes multi-select drag preparation"
Assert-FileContains -Path $mainWindow -Pattern "PrepareItemsForDragDownload\(selectedItems\)" -Description "main window uses unified drag preparation for single and multi-select"
Assert-FileContains -Path $mainWindow -Pattern "SetStorageItems\(storageItems\)" -Description "main window publishes multiple prepared storage items for drag-out"
Assert-FileContains -Path $redirectServer -Pattern "IsAllowedTarget" -Description "local redirect helper validates target server"
Assert-FileContains -Path $redirectServer -Pattern "MaxHttpRequestBytes" -Description "local redirect helper limits request size"
Assert-FileContains -Path $windowsApp -Pattern "--unregister-links" -Description "Windows link registration has uninstall cleanup entrypoint"
Assert-FileContains -Path $windowsApp -Pattern "RemoveRedirectHelperAutoStart" -Description "uninstall cleanup removes helper autorun"
Assert-FileContains -Path $windowsApp -Pattern "StartRedirectHelper" -Description "main client proactively starts redirect helper"
Assert-FileContains -Path $windowsApp -Pattern "helperMode[\s\S]*CreateRedirectServer" -Description "only redirect helper mode owns the local HTTP redirect server"
Assert-FileContains -Path $singleInstanceManager -Pattern "RYNAT_SINGLE_INSTANCE_OK" -Description "single-instance IPC requires primary acknowledgement"
Assert-FileContains -Path $singleInstanceManager -Pattern "Task<bool> SendToPrimaryAsync" -Description "single-instance forwarding reports delivery result"
Assert-FileContains -Path $cacheManagementService -Pattern "PreviewCache" -Description "cache management covers preview cache"
Assert-FileContains -Path $cacheManagementService -Pattern "DragDownloadCache" -Description "cache management covers drag download cache"
Assert-FileContains -Path $cacheManagementService -Pattern "OpenCache" -Description "cache management covers open cache"

Write-Host "Building Windows client..." -ForegroundColor Cyan
dotnet build $windowsProject -v minimal -nr:false -p:OutDir="$BuildOutputDir\"
Assert-NativeCommandSucceeded "Windows client build"

Write-Host "Windows app service smoke check passed." -ForegroundColor Green
