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
$fileListXaml = Join-Path $root "apps\windows\UI\Files\FileListView.xaml"
$fileListView = Join-Path $root "apps\windows\UI\Files\FileListView.xaml.cs"
$previewPaneView = Join-Path $root "apps\windows\UI\Preview\PreviewPaneView.xaml"
$previewPaneViewModel = Join-Path $root "apps\windows\UI\Preview\PreviewPaneViewModel.cs"
$fileOperationInterface = Join-Path $root "apps\windows\Services\FileOperations\IFileOperationService.cs"
$fileOperationService = Join-Path $root "apps\windows\Services\FileOperations\FileOperationService.cs"
$remoteCopyMoveService = Join-Path $root "apps\windows\Services\FileOperations\RemoteCopyMoveService.cs"
$fileTransferService = Join-Path $root "apps\windows\Services\FileTransfers\FileTransferService.cs"
$cacheCleanupService = Join-Path $root "apps\windows\Services\Cache\WindowsCacheCleanupService.cs"
$previewService = Join-Path $root "apps\windows\Services\Preview\PreviewService.cs"
$thumbnailServiceInterface = Join-Path $root "apps\windows\Services\Preview\IThumbnailService.cs"
$linkActivationService = Join-Path $root "apps\windows\Services\LinkActivation\LinkActivationService.cs"
$localRedirectService = Join-Path $root "apps\windows\Platform\Activation\LocalLinkRedirectService.cs"
$singleInstanceService = Join-Path $root "apps\windows\Platform\Activation\WindowsSingleInstanceService.cs"
$protocolRegistrationService = Join-Path $root "apps\windows\Platform\Activation\WindowsProtocolRegistrationService.cs"
$shellDragDropService = Join-Path $root "apps\windows\Platform\Shell\WindowsShellDragDropService.cs"
$windowsThumbnailService = Join-Path $root "apps\windows\Platform\Shell\WindowsThumbnailService.cs"
$windowsClipboardService = Join-Path $root "apps\windows\Platform\Clipboard\WindowsClipboardService.cs"

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
    "apps\windows\Domain\RemoteDragPayload.cs",
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
    "apps\windows\Services\Preview\IThumbnailService.cs",
    "apps\windows\Services\Profiles\ServerProfileService.cs",
    "apps\windows\Services\Smb\SmbSessionService.cs",
    "apps\windows\Platform\Activation\LocalLinkRedirectService.cs",
    "apps\windows\Platform\Activation\WindowsProtocolRegistrationService.cs",
    "apps\windows\Platform\Activation\WindowsSingleInstanceService.cs",
    "apps\windows\Platform\Clipboard\WindowsClipboardService.cs",
    "apps\windows\Platform\Dialogs\WindowsUserDialogService.cs",
    "apps\windows\Platform\Shell\WindowsShellDragDropService.cs",
    "apps\windows\Platform\Shell\WindowsThumbnailService.cs",
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
Assert-FileContains -Path $windowsApp -Pattern "new WindowsThumbnailService\(\)" -Description "Windows thumbnail service registered"
Assert-FileContains -Path $windowsApp -Pattern "new PreviewService\(bridge, thumbnailService\)" -Description "PreviewService registered"
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
Assert-FileContains -Path $jsonContext -Pattern "JsonSerializable\(typeof\(BridgeResponse<SmbTaskStatus>\)\)" -Description "JSON source generation covers task status"

Write-Host "Checking WPF feature plumbing..." -ForegroundColor Cyan

Assert-FileContains -Path $shellViewModel -Pattern "CopyLinkCommand" -Description "shell exposes copy-link command"
Assert-FileContains -Path $shellViewModel -Pattern "CutCommand" -Description "shell exposes cut command"
Assert-FileContains -Path $shellViewModel -Pattern "CopyFileCommand" -Description "shell exposes remote copy command"
Assert-FileContains -Path $shellViewModel -Pattern "PasteCommand" -Description "shell exposes remote paste command"
Assert-FileContains -Path $shellViewModel -Pattern "DirectoryNavigationCoordinator" -Description "shell delegates directory navigation workflow"
Assert-FileContains -Path $shellViewModel -Pattern "RemoteClipboardCoordinator" -Description "shell delegates remote clipboard workflow"
Assert-FileContains -Path $shellViewModel -Pattern "PasteRemoteClipboardAsync" -Description "shell pastes remote clipboard"
Assert-FileContains -Path $shellViewModel -Pattern "SelectedRemoteItems" -Description "shell uses multi-selection for remote clipboard commands"
Assert-FileContains -Path $directoryNavigationCoordinator -Pattern "LoadAsync" -Description "directory navigation coordinator owns directory loading"
Assert-FileContains -Path $directoryNavigationCoordinator -Pattern "CurrentShare" -Description "directory navigation coordinator tracks current share"
Assert-FileContains -Path $directoryNavigationCoordinator -Pattern "CurrentPath" -Description "directory navigation coordinator tracks current path"
Assert-FileContains -Path $directoryNavigationCoordinator -Pattern "CurrentNavigationNode" -Description "directory navigation coordinator resolves current tree node"
Assert-FileContains -Path $directoryNavigationCoordinator -Pattern "NormalizeDirectoryPath" -Description "directory navigation coordinator normalizes directory paths"
Assert-FileContains -Path $remoteClipboardCoordinator -Pattern "RemoteClipboardItem" -Description "clipboard coordinator tracks remote clipboard item"
Assert-FileContains -Path $remoteClipboardCoordinator -Pattern "foreach \(var item in clipboard\.Items\)" -Description "clipboard coordinator pastes multiple items"
Assert-FileContains -Path $remoteClipboardCoordinator -Pattern "conflictNames" -Description "clipboard coordinator batches overwrite conflicts"
Assert-FileContains -Path $remoteClipboardCoordinator -Pattern "PasteAsync" -Description "clipboard coordinator owns paste workflow"
Assert-FileContains -Path $shellViewModel -Pattern "CreateFolderCommand" -Description "shell exposes create-folder command"
Assert-FileContains -Path $shellViewModel -Pattern "DeleteCommand" -Description "shell exposes delete command"
Assert-FileContains -Path $shellViewModel -Pattern "RenameCommand" -Description "shell exposes rename command"
Assert-FileContains -Path $shellViewModel -Pattern "UploadDroppedFilesAsync" -Description "shell handles local file drop upload"
Assert-FileContains -Path $shellViewModel -Pattern "StartFileDragAsync" -Description "shell starts drag-out workflow"
Assert-FileContains -Path $shellViewModel -Pattern "GetRemoteDropEffect" -Description "shell exposes remote drag/drop effect resolution"
Assert-FileContains -Path $shellViewModel -Pattern "DropRemoteItemsAsync" -Description "shell exposes remote drag/drop commit workflow"
Assert-FileContains -Path $shellViewModel -Pattern "FileDragDropCoordinator" -Description "shell delegates file drag/drop workflow"
Assert-FileContains -Path $fileDragDropCoordinator -Pattern "StartFileDragAsync" -Description "file drag/drop coordinator owns drag-out workflow"
Assert-FileContains -Path $fileDragDropCoordinator -Pattern "GetRemoteDropEffect" -Description "file drag/drop coordinator resolves internal remote drop effects"
Assert-FileContains -Path $fileDragDropCoordinator -Pattern "DropRemoteItemsAsync" -Description "file drag/drop coordinator owns internal remote drop workflow"
Assert-FileContains -Path $fileDragDropCoordinator -Pattern "MoveAsync" -Description "internal remote drag/drop can move remote items"
Assert-FileContains -Path $fileDragDropCoordinator -Pattern "CopyAsync" -Description "internal remote drag/drop can copy remote items"
Assert-FileContains -Path $fileDragDropCoordinator -Pattern "IsInvalidRemoteDropTarget" -Description "internal remote drag/drop rejects invalid targets"
Assert-FileContains -Path $fileDragDropCoordinator -Pattern "UploadDroppedFilesAsync" -Description "file drag/drop coordinator owns drop upload workflow"
Assert-FileContains -Path $fileDragDropCoordinator -Pattern "ConfirmOverwrite" -Description "drop upload flow confirms same-name replacement"
Assert-FileContains -Path $fileDragDropCoordinator -Pattern "CreateDragDownloadPayloadAsync" -Description "drag-out flow creates virtual-file payload"
Assert-FileContains -Path $fileDragDropCoordinator -Pattern "UploadFilesAsync" -Description "drop upload flow calls upload service"
Assert-FileContains -Path $shellViewModel -Pattern "PreviewCoordinator" -Description "shell delegates preview workflow"
Assert-FileContains -Path $shellViewModel -Pattern "ActivateExternalArgumentsAsync" -Description "shell accepts external activation"
Assert-FileContains -Path $shellViewModel -Pattern "LinkActivationCoordinator" -Description "shell delegates link activation workflow"
Assert-FileContains -Path $shellViewModel -Pattern "OpenLinkRequestAsync" -Description "shell opens activated links"
Assert-FileContains -Path $shellViewModel -Pattern "SetText\(link\.HttpUrl\)" -Description "Windows copy-link uses document-friendly HTTP share links"
Assert-FileContains -Path $linkActivationCoordinator -Pattern "ActivateStartupArgumentsAsync" -Description "link activation coordinator parses startup arguments"
Assert-FileContains -Path $linkActivationCoordinator -Pattern "ConsumePendingIfPossibleAsync" -Description "link activation coordinator owns pending activation"
Assert-FileContains -Path $linkActivationCoordinator -Pattern "CanOpenWithSession" -Description "link activation coordinator checks active session"
Assert-FileContains -Path $previewCoordinator -Pattern "SelectFileAsync" -Description "preview coordinator owns selection preview flow"
Assert-FileContains -Path $previewCoordinator -Pattern "ShowPreviewLoading" -Description "preview coordinator shows loading state"
Assert-FileContains -Path $previewCoordinator -Pattern "ShowPreviewInfo" -Description "preview coordinator shows preview info"
Assert-FileContains -Path $previewCoordinator -Pattern "ShowPreviewUnavailable" -Description "preview coordinator handles preview failures"

Assert-FileContains -Path $fileListView -Pattern "PreviewMouseMove" -Description "file list starts drag from pointer movement"
Assert-FileContains -Path $fileListXaml -Pattern "SelectionMode=`"Extended`"" -Description "file list supports extended selection"
Assert-FileContains -Path $fileListView -Pattern "RemoteDragPayload" -Description "file list recognizes internal remote drag payloads"
Assert-FileContains -Path $fileListView -Pattern "TryGetDirectoryDropTarget" -Description "file list resolves directory drop targets"
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
Assert-FileContains -Path $shellDragDropService -Pattern "RemoteDragPayload\.DataFormat" -Description "drag service publishes internal remote drag payload"

Assert-FileContains -Path $previewService -Pattern "PreviewCache" -Description "preview service uses preview cache"
Assert-FileContains -Path $previewService -Pattern "SmbCacheFile" -Description "preview service caches remote files through core"
Assert-FileContains -Path $previewService -Pattern "CreateImageThumbnail" -Description "preview service generates lightweight image thumbnails"
Assert-FileContains -Path $previewService -Pattern "CreateVideoPoster" -Description "preview service generates video poster thumbnails"
Assert-FileContains -Path $previewService -Pattern "TryCreateThumbnail" -Description "preview service delegates video thumbnails to platform shell"
Assert-FileContains -Path $previewService -Pattern "DecodePixelWidth" -Description "preview service decodes scaled image previews"
Assert-FileContains -Path $previewService -Pattern "图片较大，暂不自动缓存预览" -Description "preview service skips large automatic image preview caching"
Assert-FileContains -Path $previewService -Pattern "InlineVideoPreviewMaxBytes" -Description "preview service caps inline video preview caching"
Assert-FileContains -Path $previewService -Pattern "暂不自动缓存预览" -Description "preview service skips large automatic video caching"
Assert-FileContains -Path $previewService -Pattern "maxBytes" -Description "preview service limits preview cache bytes"
Assert-FileContains -Path $thumbnailServiceInterface -Pattern "TryCreateThumbnail" -Description "thumbnail abstraction exposes platform thumbnail creation"
Assert-FileContains -Path $windowsThumbnailService -Pattern "IShellItemImageFactory" -Description "Windows thumbnail service uses shell thumbnail extraction"
Assert-FileContains -Path $windowsThumbnailService -Pattern "SHCreateItemFromParsingName" -Description "Windows thumbnail service creates shell items from local paths"
Assert-FileContains -Path $windowsThumbnailService -Pattern "DeleteObject" -Description "Windows thumbnail service releases shell HBITMAP handles"
Assert-FileContains -Path $previewPaneView -Pattern "ShouldShowImagePreview" -Description "preview pane shows video poster before playback"
Assert-FileContains -Path $previewPaneView -Pattern "ShouldShowVideoPreview" -Description "preview pane shows video control only during playback"
Assert-FileContains -Path $previewPaneViewModel -Pattern "IsVideoPlaying" -Description "preview state tracks video playback display mode"
Assert-FileContains -Path $previewService -Pattern "CleanupDirectory" -Description "preview cache cleanup is triggered"
Assert-FileContains -Path $cacheCleanupService -Pattern "PartialFileMaxAge" -Description "cache cleanup removes stale partial files"
Assert-FileContains -Path $cacheCleanupService -Pattern "DeleteEmptyDirectories" -Description "cache cleanup prunes empty directories"

Assert-FileContains -Path $linkActivationService -Pattern "rynat://" -Description "link activation accepts rynat protocol"
Assert-FileContains -Path $linkActivationService -Pattern "http://" -Description "link activation accepts local HTTP links"
Assert-FileContains -Path $localRedirectService -Pattern "TcpListener" -Description "local redirect owns local HTTP listener"
Assert-FileContains -Path $localRedirectService -Pattern "RedirectPageRequest\(deepLink, AlreadyActivated: true\)" -Description "local redirect serves the already-activated close page"
Assert-FileContains -Path $localRedirectService -Pattern "text/html; charset=utf-8" -Description "local redirect returns browser-close HTML after activation"
Assert-FileContains -Path $singleInstanceService -Pattern "NamedPipe" -Description "single-instance forwarding uses named pipes"

Write-Host "Checking Windows activation plumbing..." -ForegroundColor Cyan

Assert-FileContains -Path $windowsApp -Pattern "StartAsync\(e\.Args\)" -Description "single-instance service receives startup arguments"
Assert-FileContains -Path $windowsApp -Pattern "InitializeAsync\(e\.Args\)" -Description "shell receives startup arguments"
Assert-FileContains -Path $windowsApp -Pattern "ActivateArguments\(viewModel, args\.Arguments" -Description "external activations route through app"
Assert-FileContains -Path $windowsApp -Pattern "BringToFront\(activeWindow\)" -Description "external activation foregrounds main window"
Assert-FileContains -Path $windowsApp -Pattern "ActivateExternalArgumentsAsync\(arguments\)" -Description "external activation reaches shell view model"

Assert-FileContains -Path $linkActivationService -Pattern "TryExtractStartupLink" -Description "link activation extracts startup links"
Assert-FileContains -Path $linkActivationService -Pattern "Split\(' '" -Description "link activation extracts embedded links from argument strings"
Assert-FileContains -Path $linkActivationService -Pattern "NormalizeRawLink" -Description "link activation normalizes raw links"
Assert-FileContains -Path $linkActivationService -Pattern "rynat://s/\?" -Description "link activation normalizes malformed compact deep link separator"
Assert-FileContains -Path $linkActivationService -Pattern "https://" -Description "link activation accepts public HTTPS links"
Assert-FileContains -Path $linkActivationService -Pattern "CanOpenWithSession" -Description "link activation validates active session endpoint"

Assert-FileContains -Path $localRedirectService -Pattern "IPAddress\.Loopback" -Description "local redirect binds to loopback only"
Assert-FileContains -Path $localRedirectService -Pattern "MaxRequestLineBytes" -Description "local redirect caps request line size"
Assert-FileContains -Path $localRedirectService -Pattern "RequestTimeout" -Description "local redirect uses request timeout"
Assert-FileContains -Path $localRedirectService -Pattern "TryBuildDeepLink" -Description "local redirect builds deep link"
Assert-FileContains -Path $localRedirectService -Pattern "GET" -Description "local redirect only accepts GET"
Assert-FileContains -Path $localRedirectService -Pattern "Uri\.TryCreate" -Description "local redirect parses local URL safely"
Assert-FileContains -Path $localRedirectService -Pattern "rynat://s" -Description "local redirect emits rynat deep link"
Assert-FileContains -Path $localRedirectService -Pattern "404 Not Found" -Description "local redirect rejects unsupported paths"

Assert-FileContains -Path $singleInstanceService -Pattern "MutexName" -Description "single-instance uses mutex"
Assert-FileContains -Path $singleInstanceService -Pattern "PipeName" -Description "single-instance uses named pipe"
Assert-FileContains -Path $singleInstanceService -Pattern "ForwardAsync" -Description "secondary instance forwards activation arguments"
Assert-FileContains -Path $singleInstanceService -Pattern "JsonSerializer\.Serialize" -Description "single-instance serializes forwarded arguments"
Assert-FileContains -Path $singleInstanceService -Pattern "JsonSerializer\.Deserialize<string\[\]>" -Description "single-instance deserializes forwarded arguments"
Assert-FileContains -Path $singleInstanceService -Pattern "Activated\?\.Invoke" -Description "single-instance raises activation event"

Assert-FileContains -Path $protocolRegistrationService -Pattern "URL Protocol" -Description "protocol registration marks URL protocol"
Assert-FileContains -Path $protocolRegistrationService -Pattern "shell\\open\\command" -Description "protocol registration writes open command"
Assert-FileContains -Path $protocolRegistrationService -Pattern "%1" -Description "protocol registration forwards original activation argument"

if ($Build) {
    Write-Host "Building Windows client..." -ForegroundColor Cyan
    dotnet build $windowsProject -v minimal -nr:false -p:OutDir="$BuildOutputDir\"
    Assert-NativeCommandSucceeded "Windows client build"
}
else {
    Write-Host "Skipping Windows build. Pass -Build from a Windows/.NET environment to compile the WPF client." -ForegroundColor Yellow
}

Write-Host "Windows WPF app service smoke check passed." -ForegroundColor Green
