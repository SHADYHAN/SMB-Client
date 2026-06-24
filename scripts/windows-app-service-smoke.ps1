param(
    [switch]$Build,
    [string]$BuildOutputDir
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$surfaceCheckScript = Join-Path $root "scripts\check-bridge-surface.ps1"
$windowsProject = Join-Path $root "apps\windows\Rynat.WindowsClient.csproj"
$windowsApp = Join-Path $root "apps\windows\App.xaml.cs"
$nativeMethods = Join-Path $root "apps\windows\CoreAdapter\NativeMethods.cs"
$coreBridge = Join-Path $root "apps\windows\CoreAdapter\RynatCoreBridge.cs"
$jsonContext = Join-Path $root "apps\windows\CoreAdapter\RynatJsonContext.cs"
$shellViewModel = Join-Path $root "apps\windows\UI\Shell\ShellViewModel.cs"
$directoryNavigationCoordinator = Join-Path $root "apps\windows\UI\Shell\DirectoryNavigationCoordinator.cs"
$fileDragDropCoordinator = Join-Path $root "apps\windows\UI\Shell\FileDragDropCoordinator.cs"
$linkActivationCoordinator = Join-Path $root "apps\windows\UI\Shell\LinkActivationCoordinator.cs"
$previewCoordinator = Join-Path $root "apps\windows\UI\Shell\PreviewCoordinator.cs"
$remoteClipboardCoordinator = Join-Path $root "apps\windows\UI\Shell\RemoteClipboardCoordinator.cs"
$mainWindow = Join-Path $root "apps\windows\MainWindow.xaml.cs"
$fileListView = Join-Path $root "apps\windows\UI\Files\FileListView.xaml.cs"
$fileOperationInterface = Join-Path $root "apps\windows\Services\FileOperations\IFileOperationService.cs"
$fileOperationService = Join-Path $root "apps\windows\Services\FileOperations\FileOperationService.cs"
$remoteCopyMoveService = Join-Path $root "apps\windows\Services\FileOperations\RemoteCopyMoveService.cs"
$fileTransferService = Join-Path $root "apps\windows\Services\FileTransfers\FileTransferService.cs"
$cacheCleanupService = Join-Path $root "apps\windows\Services\Cache\WindowsCacheCleanupService.cs"
$previewService = Join-Path $root "apps\windows\Services\Preview\PreviewService.cs"
$linkActivationService = Join-Path $root "apps\windows\Services\LinkActivation\LinkActivationService.cs"
$localRedirectService = Join-Path $root "apps\windows\Platform\Activation\LocalLinkRedirectService.cs"
$singleInstanceService = Join-Path $root "apps\windows\Platform\Activation\WindowsSingleInstanceService.cs"
$shellDragDropService = Join-Path $root "apps\windows\Platform\Shell\WindowsShellDragDropService.cs"

if ([string]::IsNullOrWhiteSpace($BuildOutputDir)) {
    $BuildOutputDir = Join-Path $root "apps\windows\bin\AppServiceSmoke"
}

function Assert-FileExists {
    param(
        [string]$Path,
        [string]$Description
    )

    if (-not (Test-Path $Path)) {
        throw "Missing file: $Path"
    }

    Write-Host "OK: $Description"
}

function Assert-FileContains {
    param(
        [string]$Path,
        [string]$Pattern,
        [string]$Description
    )

    Assert-FileExists -Path $Path -Description $Description | Out-Null

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
& (Get-Process -Id $PID).Path -ExecutionPolicy Bypass -File $surfaceCheckScript
Assert-NativeCommandSucceeded "Bridge surface check"

Write-Host "Checking WPF Windows client structure..." -ForegroundColor Cyan

$requiredFiles = @(
    "apps\windows\Rynat.WindowsClient.csproj",
    "apps\windows\App.xaml.cs",
    "apps\windows\MainWindow.xaml",
    "apps\windows\MainWindow.xaml.cs",
    "apps\windows\CoreAdapter\NativeMethods.cs",
    "apps\windows\CoreAdapter\RynatCoreBridge.cs",
    "apps\windows\CoreAdapter\RynatCoreRequests.cs",
    "apps\windows\CoreAdapter\RynatCoreModels.cs",
    "apps\windows\CoreAdapter\RynatJsonContext.cs",
    "apps\windows\Domain\RemoteFileItem.cs",
    "apps\windows\Domain\RemoteClipboardItem.cs",
    "apps\windows\Domain\RemoteClipboardMode.cs",
    "apps\windows\Domain\ServerSession.cs",
    "apps\windows\Services\Bootstrap\BootstrapService.cs",
    "apps\windows\Services\Directory\DirectoryService.cs",
    "apps\windows\Services\FileOperations\FileOperationService.cs",
    "apps\windows\Services\FileOperations\IRemoteCopyMoveService.cs",
    "apps\windows\Services\FileOperations\RemoteCopyMoveService.cs",
    "apps\windows\Services\Cache\WindowsCacheCleanupService.cs",
    "apps\windows\Services\FileTransfers\FileTransferService.cs",
    "apps\windows\Services\LinkActivation\LinkActivationService.cs",
    "apps\windows\Services\Links\QuickLinkService.cs",
    "apps\windows\Services\Preview\PreviewService.cs",
    "apps\windows\Services\Profiles\ServerProfileService.cs",
    "apps\windows\Services\Smb\SmbSessionService.cs",
    "apps\windows\Platform\Activation\LocalLinkRedirectService.cs",
    "apps\windows\Platform\Activation\WindowsProtocolRegistrationService.cs",
    "apps\windows\Platform\Activation\WindowsSingleInstanceService.cs",
    "apps\windows\Platform\Clipboard\WindowsClipboardService.cs",
    "apps\windows\Platform\Dialogs\WindowsUserDialogService.cs",
    "apps\windows\Platform\Shell\WindowsShellDragDropService.cs",
    "apps\windows\Platform\Shell\WindowsWindowForegroundService.cs",
    "apps\windows\UI\Shell\ShellViewModel.cs",
    "apps\windows\UI\Shell\DirectoryNavigationCoordinator.cs",
    "apps\windows\UI\Shell\FileDragDropCoordinator.cs",
    "apps\windows\UI\Shell\LinkActivationCoordinator.cs",
    "apps\windows\UI\Shell\PreviewCoordinator.cs",
    "apps\windows\UI\Shell\RemoteClipboardCoordinator.cs",
    "apps\windows\UI\Shell\RemoteClipboardPasteResult.cs",
    "apps\windows\UI\Files\FileListView.xaml",
    "apps\windows\UI\Files\FileListView.xaml.cs",
    "apps\windows\UI\Navigation\NavigationTreeView.xaml",
    "apps\windows\UI\Preview\PreviewPaneView.xaml",
    "apps\windows\UI\Status\StatusBarView.xaml"
)

foreach ($relativePath in $requiredFiles) {
    Assert-FileExists -Path (Join-Path $root $relativePath) -Description $relativePath
}

Write-Host "Checking service registration and app boundaries..." -ForegroundColor Cyan

Assert-FileContains -Path $windowsProject -Pattern "<UseWPF>true</UseWPF>" -Description "Windows client is WPF"
Assert-FileContains -Path $windowsProject -Pattern "cargo build -p rynat-core" -Description "Windows build invokes Rust core build"
Assert-FileContains -Path $windowsProject -Pattern "rynat_core\.dll" -Description "Windows build copies rynat_core.dll"

Assert-FileContains -Path $windowsApp -Pattern "new RynatCoreBridge\(\)" -Description "App creates one core bridge"
Assert-FileContains -Path $windowsApp -Pattern "new BootstrapService\(bridge\)" -Description "BootstrapService registered"
Assert-FileContains -Path $windowsApp -Pattern "new SmbSessionService\(bridge\)" -Description "SmbSessionService registered"
Assert-FileContains -Path $windowsApp -Pattern "new DirectoryService\(bridge\)" -Description "DirectoryService registered"
Assert-FileContains -Path $windowsApp -Pattern "new RemoteCopyMoveService\(bridge\)" -Description "RemoteCopyMoveService registered"
Assert-FileContains -Path $windowsApp -Pattern "new FileOperationService\(bridge\)" -Description "FileOperationService registered"
Assert-FileContains -Path $windowsApp -Pattern "new FileTransferService\(bridge\)" -Description "FileTransferService registered"
Assert-FileContains -Path $windowsApp -Pattern "new QuickLinkService\(bridge\)" -Description "QuickLinkService registered"
Assert-FileContains -Path $windowsApp -Pattern "new LinkActivationService\(bridge\)" -Description "LinkActivationService registered"
Assert-FileContains -Path $windowsApp -Pattern "new PreviewService\(bridge\)" -Description "PreviewService registered"
Assert-FileContains -Path $windowsApp -Pattern "new ServerProfileService\(bridge\)" -Description "ServerProfileService registered"
Assert-FileContains -Path $windowsApp -Pattern "new LocalLinkRedirectService\(bridge\)" -Description "local HTTP redirect service registered"
Assert-FileContains -Path $windowsApp -Pattern "WindowsSingleInstanceService" -Description "single-instance service registered"
Assert-FileContains -Path $windowsApp -Pattern "WindowsWindowForegroundService" -Description "foreground activation adapter registered"

Assert-FileContains -Path $mainWindow -Pattern "ShellViewModel" -Description "MainWindow depends only on shell view model"

Write-Host "Checking bridge coverage..." -ForegroundColor Cyan

Assert-FileContains -Path $nativeMethods -Pattern "rynat_smb_copy_file_json" -Description "NativeMethods exposes remote copy"
Assert-FileContains -Path $nativeMethods -Pattern "rynat_smb_start_task_json" -Description "NativeMethods exposes task API"
Assert-FileContains -Path $coreBridge -Pattern "public SmbWriteResult SmbCopyFile" -Description "C# bridge wraps remote copy"
Assert-FileContains -Path $coreBridge -Pattern "public SmbTaskStartResult SmbStartTask" -Description "C# bridge wraps task start"
Assert-FileContains -Path $jsonContext -Pattern "JsonSerializable\(typeof\(SmbCopyFileRequest\)\)" -Description "JSON source generation covers copy request"
Assert-FileContains -Path $jsonContext -Pattern "JsonSerializable\(typeof\(SmbTaskStatus\)\)" -Description "JSON source generation covers task status"

Write-Host "Checking WPF feature plumbing..." -ForegroundColor Cyan

Assert-FileContains -Path $shellViewModel -Pattern "CopyLinkCommand" -Description "shell exposes copy-link command"
Assert-FileContains -Path $shellViewModel -Pattern "CutCommand" -Description "shell exposes cut command"
Assert-FileContains -Path $shellViewModel -Pattern "CopyFileCommand" -Description "shell exposes remote copy command"
Assert-FileContains -Path $shellViewModel -Pattern "PasteCommand" -Description "shell exposes remote paste command"
Assert-FileContains -Path $shellViewModel -Pattern "DirectoryNavigationCoordinator" -Description "shell delegates directory navigation workflow"
Assert-FileContains -Path $shellViewModel -Pattern "RemoteClipboardCoordinator" -Description "shell delegates remote clipboard workflow"
Assert-FileContains -Path $shellViewModel -Pattern "PasteRemoteClipboardAsync" -Description "shell pastes remote clipboard"
Assert-FileContains -Path $directoryNavigationCoordinator -Pattern "LoadAsync" -Description "directory navigation coordinator owns directory loading"
Assert-FileContains -Path $directoryNavigationCoordinator -Pattern "CurrentShare" -Description "directory navigation coordinator tracks current share"
Assert-FileContains -Path $directoryNavigationCoordinator -Pattern "CurrentPath" -Description "directory navigation coordinator tracks current path"
Assert-FileContains -Path $directoryNavigationCoordinator -Pattern "CurrentNavigationNode" -Description "directory navigation coordinator resolves current tree node"
Assert-FileContains -Path $directoryNavigationCoordinator -Pattern "NormalizeDirectoryPath" -Description "directory navigation coordinator normalizes directory paths"
Assert-FileContains -Path $remoteClipboardCoordinator -Pattern "RemoteClipboardItem" -Description "clipboard coordinator tracks remote clipboard item"
Assert-FileContains -Path $remoteClipboardCoordinator -Pattern "PasteAsync" -Description "clipboard coordinator owns paste workflow"
Assert-FileContains -Path $shellViewModel -Pattern "CreateFolderCommand" -Description "shell exposes create-folder command"
Assert-FileContains -Path $shellViewModel -Pattern "DeleteCommand" -Description "shell exposes delete command"
Assert-FileContains -Path $shellViewModel -Pattern "RenameCommand" -Description "shell exposes rename command"
Assert-FileContains -Path $shellViewModel -Pattern "UploadDroppedFilesAsync" -Description "shell handles local file drop upload"
Assert-FileContains -Path $shellViewModel -Pattern "StartFileDragAsync" -Description "shell starts drag-out workflow"
Assert-FileContains -Path $shellViewModel -Pattern "FileDragDropCoordinator" -Description "shell delegates file drag/drop workflow"
Assert-FileContains -Path $fileDragDropCoordinator -Pattern "StartFileDragAsync" -Description "file drag/drop coordinator owns drag-out workflow"
Assert-FileContains -Path $fileDragDropCoordinator -Pattern "UploadDroppedFilesAsync" -Description "file drag/drop coordinator owns drop upload workflow"
Assert-FileContains -Path $fileDragDropCoordinator -Pattern "ConfirmOverwrite" -Description "drop upload flow confirms same-name replacement"
Assert-FileContains -Path $fileDragDropCoordinator -Pattern "CreateDragDownloadPayloadAsync" -Description "drag-out flow creates virtual-file payload"
Assert-FileContains -Path $fileDragDropCoordinator -Pattern "UploadFilesAsync" -Description "drop upload flow calls upload service"
Assert-FileContains -Path $shellViewModel -Pattern "PreviewCoordinator" -Description "shell delegates preview workflow"
Assert-FileContains -Path $shellViewModel -Pattern "ActivateExternalArgumentsAsync" -Description "shell accepts external activation"
Assert-FileContains -Path $shellViewModel -Pattern "LinkActivationCoordinator" -Description "shell delegates link activation workflow"
Assert-FileContains -Path $shellViewModel -Pattern "OpenLinkRequestAsync" -Description "shell opens activated links"
Assert-FileContains -Path $linkActivationCoordinator -Pattern "ActivateStartupArgumentsAsync" -Description "link activation coordinator parses startup arguments"
Assert-FileContains -Path $linkActivationCoordinator -Pattern "ConsumePendingIfPossibleAsync" -Description "link activation coordinator owns pending activation"
Assert-FileContains -Path $linkActivationCoordinator -Pattern "CanOpenWithSession" -Description "link activation coordinator checks active session"
Assert-FileContains -Path $previewCoordinator -Pattern "SelectFileAsync" -Description "preview coordinator owns selection preview flow"
Assert-FileContains -Path $previewCoordinator -Pattern "ShowPreviewLoading" -Description "preview coordinator shows loading state"
Assert-FileContains -Path $previewCoordinator -Pattern "ShowPreviewInfo" -Description "preview coordinator shows preview info"
Assert-FileContains -Path $previewCoordinator -Pattern "ShowPreviewUnavailable" -Description "preview coordinator handles preview failures"

Assert-FileContains -Path $fileListView -Pattern "PreviewMouseMove" -Description "file list starts drag from pointer movement"
Assert-FileContains -Path $fileListView -Pattern "DataFormats\.FileDrop" -Description "file list accepts local file drops"
Assert-FileContains -Path $fileListView -Pattern "Key\.Delete" -Description "file list handles delete key"
Assert-FileContains -Path $fileListView -Pattern "Key\.F2" -Description "file list handles rename shortcut"
Assert-FileContains -Path $fileListView -Pattern "Key\.F5" -Description "file list handles refresh shortcut"
Assert-FileContains -Path $fileListView -Pattern "Key\.X" -Description "file list handles cut shortcut"
Assert-FileContains -Path $fileListView -Pattern "Key\.C" -Description "file list handles remote copy shortcut"
Assert-FileContains -Path $fileListView -Pattern "Key\.V" -Description "file list handles paste shortcut"

Assert-FileContains -Path $fileOperationInterface -Pattern "CreateDirectoryAsync" -Description "file operation interface supports create folder"
Assert-FileContains -Path $fileOperationInterface -Pattern "DeleteAsync" -Description "file operation interface supports delete"
Assert-FileContains -Path $fileOperationInterface -Pattern "RenameAsync" -Description "file operation interface supports rename"
Assert-FileContains -Path $fileOperationInterface -Pattern "UploadFilesAsync" -Description "file operation interface supports upload"
Assert-FileContains -Path $fileOperationService -Pattern "SmbCreateDirectory" -Description "file operation service calls core create-directory"
Assert-FileContains -Path $fileOperationService -Pattern "SmbDelete" -Description "file operation service calls core delete"
Assert-FileContains -Path $fileOperationService -Pattern "SmbRename" -Description "file operation service calls core rename"
Assert-FileContains -Path $fileOperationService -Pattern "SmbUploadFile" -Description "file operation service calls core upload"
Assert-FileContains -Path $remoteCopyMoveService -Pattern "SmbCopyFile" -Description "remote copy/move service calls core copy"
Assert-FileContains -Path $remoteCopyMoveService -Pattern "SmbRename" -Description "remote copy/move service calls core move"
Assert-FileContains -Path $remoteCopyMoveService -Pattern "CopyDirectoryRecursive" -Description "remote copy/move service copies directories recursively"
Assert-FileContains -Path $remoteCopyMoveService -Pattern "DeleteRemotePathRecursive" -Description "remote copy/move service replaces directories recursively"
Assert-FileContains -Path $remoteCopyMoveService -Pattern "cross_share_move" -Description "remote copy/move service rejects cross-share move"
Assert-FileContains -Path $fileOperationService -Pattern "replaceExisting" -Description "file operation service passes upload overwrite intent"

Assert-FileContains -Path $fileTransferService -Pattern "LazyRemoteDownloadStream" -Description "drag-out uses lazy virtual-file download stream"
Assert-FileContains -Path $fileTransferService -Pattern "DragCache" -Description "drag-out has local drag cache path"
Assert-FileContains -Path $fileTransferService -Pattern "CleanupDirectory" -Description "drag cache cleanup is triggered"
Assert-FileContains -Path $fileTransferService -Pattern "\.part" -Description "drag-out downloads to partial file before completion"
Assert-FileContains -Path $shellDragDropService -Pattern "FileGroupDescriptorW" -Description "drag-out publishes virtual file descriptor"
Assert-FileContains -Path $shellDragDropService -Pattern "FileContents" -Description "drag-out publishes virtual file contents"

Assert-FileContains -Path $previewService -Pattern "PreviewCache" -Description "preview service uses preview cache"
Assert-FileContains -Path $previewService -Pattern "SmbCacheFile" -Description "preview service caches remote files through core"
Assert-FileContains -Path $previewService -Pattern "maxBytes" -Description "preview service limits preview cache bytes"
Assert-FileContains -Path $previewService -Pattern "CleanupDirectory" -Description "preview cache cleanup is triggered"
Assert-FileContains -Path $cacheCleanupService -Pattern "PartialFileMaxAge" -Description "cache cleanup removes stale partial files"
Assert-FileContains -Path $cacheCleanupService -Pattern "DeleteEmptyDirectories" -Description "cache cleanup prunes empty directories"

Assert-FileContains -Path $linkActivationService -Pattern "rynat://" -Description "link activation accepts rynat protocol"
Assert-FileContains -Path $linkActivationService -Pattern "http://" -Description "link activation accepts local HTTP links"
Assert-FileContains -Path $localRedirectService -Pattern "TcpListener" -Description "local redirect owns local HTTP listener"
Assert-FileContains -Path $localRedirectService -Pattern "AlreadyActivated:\s*true" -Description "local redirect returns already-activated close page"
Assert-FileContains -Path $singleInstanceService -Pattern "NamedPipe" -Description "single-instance forwarding uses named pipes"

if ($Build) {
    Write-Host "Building Windows client..." -ForegroundColor Cyan
    dotnet build $windowsProject -v minimal -nr:false -p:OutDir="$BuildOutputDir\"
    Assert-NativeCommandSucceeded "Windows client build"
}
else {
    Write-Host "Skipping Windows build. Pass -Build from a Windows/.NET environment to compile the WPF client." -ForegroundColor Yellow
}

Write-Host "Windows WPF app service smoke check passed." -ForegroundColor Green
