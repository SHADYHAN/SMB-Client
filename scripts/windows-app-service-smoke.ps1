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
$loginCoordinator = Join-Path $root "apps\windows\UI\Shell\LoginCoordinator.cs"
$directoryNavigationCoordinator = Join-Path $root "apps\windows\UI\Shell\DirectoryNavigationCoordinator.cs"
$directoryService = Join-Path $root "apps\windows\Services\Directory\DirectoryService.cs"
$fileDragDropCoordinator = Join-Path $root "apps\windows\UI\Shell\FileDragDropCoordinator.cs"
$linkActivationCoordinator = Join-Path $root "apps\windows\UI\Shell\LinkActivationCoordinator.cs"
$previewCoordinator = Join-Path $root "apps\windows\UI\Shell\PreviewCoordinator.cs"
$remoteClipboardCoordinator = Join-Path $root "apps\windows\UI\Shell\RemoteClipboardCoordinator.cs"
$mainWindowXaml = Join-Path $root "apps\windows\MainWindow.xaml"
$mainWindow = Join-Path $root "apps\windows\MainWindow.xaml.cs"
$loginViewModel = Join-Path $root "apps\windows\UI\Login\LoginViewModel.cs"
$loginView = Join-Path $root "apps\windows\UI\Login\LoginView.xaml.cs"
$fileListViewModel = Join-Path $root "apps\windows\UI\Files\FileListViewModel.cs"
$fileListXaml = Join-Path $root "apps\windows\UI\Files\FileListView.xaml"
$fileListView = Join-Path $root "apps\windows\UI\Files\FileListView.xaml.cs"
$navigationNodeViewModel = Join-Path $root "apps\windows\UI\Navigation\NavigationNodeViewModel.cs"
$navigationTreeXaml = Join-Path $root "apps\windows\UI\Navigation\NavigationTreeView.xaml"
$navigationTreeView = Join-Path $root "apps\windows\UI\Navigation\NavigationTreeView.xaml.cs"
$previewPaneView = Join-Path $root "apps\windows\UI\Preview\PreviewPaneView.xaml"
$previewPaneViewModel = Join-Path $root "apps\windows\UI\Preview\PreviewPaneViewModel.cs"
$rynatTheme = Join-Path $root "apps\windows\UI\Styles\RynatTheme.xaml"
$fileOperationInterface = Join-Path $root "apps\windows\Services\FileOperations\IFileOperationService.cs"
$fileOperationService = Join-Path $root "apps\windows\Services\FileOperations\FileOperationService.cs"
$remoteCopyMoveService = Join-Path $root "apps\windows\Services\FileOperations\RemoteCopyMoveService.cs"
$fileTransferService = Join-Path $root "apps\windows\Services\FileTransfers\FileTransferService.cs"
$cacheCleanupService = Join-Path $root "apps\windows\Services\Cache\WindowsCacheCleanupService.cs"
$previewService = Join-Path $root "apps\windows\Services\Preview\PreviewService.cs"
$thumbnailServiceInterface = Join-Path $root "apps\windows\Services\Preview\IThumbnailService.cs"
$quickLinkService = Join-Path $root "apps\windows\Services\Links\QuickLinkService.cs"
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

function Assert-FileNotContains {
    param(
        [string]$Path,
        [string]$Pattern,
        [string]$Description
    )

    Assert-FileExists -Path $Path -Description $Description | Out-Null

    $content = Get-Content -Raw -Encoding UTF8 $Path
    if ($content -match $Pattern) {
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
    "apps\windows\Domain\FavoriteLinkItem.cs",
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
    "apps\windows\Services\Smb\ISmbTaskService.cs",
    "apps\windows\Services\Smb\SmbSessionService.cs",
    "apps\windows\Services\Smb\SmbTaskOperation.cs",
    "apps\windows\Services\Smb\SmbTaskService.cs",
    "apps\windows\Platform\Activation\LocalLinkRedirectService.cs",
    "apps\windows\Platform\Activation\WindowsProtocolRegistrationService.cs",
    "apps\windows\Platform\Activation\WindowsSingleInstanceService.cs",
    "apps\windows\Platform\Clipboard\WindowsClipboardService.cs",
    "apps\windows\Platform\Dialogs\WindowsUserDialogService.cs",
    "apps\windows\Platform\Shell\WindowsShellDragDropService.cs",
    "apps\windows\Platform\Shell\WindowsThumbnailService.cs",
    "apps\windows\Platform\Shell\WindowsWindowForegroundService.cs",
    "apps\windows\UI\Shell\ShellViewModel.cs",
    "apps\windows\UI\Shell\LoginCoordinator.cs",
    "apps\windows\UI\Shell\DirectoryNavigationCoordinator.cs",
    "apps\windows\UI\Shell\FileDragDropCoordinator.cs",
    "apps\windows\UI\Shell\LinkActivationCoordinator.cs",
    "apps\windows\UI\Shell\PreviewCoordinator.cs",
    "apps\windows\UI\Shell\RemoteClipboardCoordinator.cs",
    "apps\windows\UI\Shell\RemoteClipboardPasteResult.cs",
    "apps\windows\UI\Files\FileListView.xaml",
    "apps\windows\UI\Files\FileListView.xaml.cs",
    "apps\windows\UI\Navigation\FavoriteLinkViewModel.cs",
    "apps\windows\UI\Navigation\NavigationSidebarTab.cs",
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
Assert-FileContains -Path $windowsApp -Pattern "new SmbTaskService\(bridge\)" -Description "SmbTaskService registered"
Assert-FileContains -Path $windowsApp -Pattern "new DirectoryService\(bridge\)" -Description "DirectoryService registered"
Assert-FileContains -Path $windowsApp -Pattern "new RemoteCopyMoveService\(bridge\)" -Description "RemoteCopyMoveService registered"
Assert-FileContains -Path $windowsApp -Pattern "new FileOperationService\(taskService\)" -Description "FileOperationService registered"
Assert-FileContains -Path $windowsApp -Pattern "new FileTransferService\(taskService\)" -Description "FileTransferService registered"
Assert-FileContains -Path $windowsApp -Pattern "new QuickLinkService\(bridge\)" -Description "QuickLinkService registered"
Assert-FileContains -Path $windowsApp -Pattern "new LinkActivationService\(bridge\)" -Description "LinkActivationService registered"
Assert-FileContains -Path $windowsApp -Pattern "new WindowsThumbnailService\(\)" -Description "Windows thumbnail service registered"
Assert-FileContains -Path $windowsApp -Pattern "new PreviewService\(bridge, thumbnailService\)" -Description "PreviewService registered"
Assert-FileContains -Path $windowsApp -Pattern "new ServerProfileService\(bridge\)" -Description "ServerProfileService registered"
Assert-FileContains -Path $windowsApp -Pattern "new LocalLinkRedirectService\(bridge\)" -Description "local HTTP redirect service registered"
Assert-FileContains -Path $windowsApp -Pattern "WindowsSingleInstanceService" -Description "single-instance service registered"
Assert-FileContains -Path $windowsApp -Pattern "WindowsWindowForegroundService" -Description "foreground activation adapter registered"

Assert-FileContains -Path $mainWindow -Pattern "ShellViewModel" -Description "MainWindow depends only on shell view model"
Assert-FileContains -Path $mainWindow -Pattern "UpdatePreviewColumn" -Description "MainWindow only owns preview column view sizing"
Assert-FileNotContains -Path $mainWindow -Pattern "Services\.|Platform\.|RynatCoreBridge|Smb|DirectoryService|FileOperation" -Description "MainWindow stays free of service/platform logic"

Write-Host "Checking bridge coverage..." -ForegroundColor Cyan

Assert-FileContains -Path $nativeMethods -Pattern "rynat_smb_copy_file_json" -Description "NativeMethods exposes remote copy"
Assert-FileContains -Path $nativeMethods -Pattern "rynat_smb_start_task_json" -Description "NativeMethods exposes task API"
Assert-FileContains -Path $coreBridge -Pattern "public SmbWriteResult SmbCopyFile" -Description "C# bridge wraps remote copy"
Assert-FileContains -Path $coreBridge -Pattern "public SmbTaskStartResult SmbStartTask" -Description "C# bridge wraps task start"
Assert-FileContains -Path $coreBridge -Pattern "public SmbTaskStatus SmbPollTask" -Description "C# bridge wraps task poll"
Assert-FileContains -Path $coreBridge -Pattern "public SmbTaskStatus SmbCancelTask" -Description "C# bridge wraps task cancel"
Assert-FileContains -Path $coreBridge -Pattern "public void SmbClearTask" -Description "C# bridge wraps task clear"
Assert-FileContains -Path $jsonContext -Pattern "JsonSerializable\(typeof\(SmbCopyFileRequest\)\)" -Description "JSON source generation covers copy request"
Assert-FileContains -Path $jsonContext -Pattern "JsonSerializable\(typeof\(BridgeResponse<SmbTaskStatus>\)\)" -Description "JSON source generation covers task status"

Write-Host "Checking WPF feature plumbing..." -ForegroundColor Cyan

Assert-FileContains -Path $shellViewModel -Pattern "CopyLinkCommand" -Description "shell exposes copy-link command"
Assert-FileContains -Path $shellViewModel -Pattern "CutCommand" -Description "shell exposes cut command"
Assert-FileContains -Path $shellViewModel -Pattern "CopyFileCommand" -Description "shell exposes remote copy command"
Assert-FileContains -Path $shellViewModel -Pattern "PasteCommand" -Description "shell exposes remote paste command"
Assert-FileContains -Path $shellViewModel -Pattern "GoUpCommand" -Description "shell exposes parent-directory command"
Assert-FileContains -Path $shellViewModel -Pattern "GoShareRootCommand" -Description "shell exposes share-root command"
Assert-FileContains -Path $shellViewModel -Pattern "LoginCoordinator" -Description "shell delegates login workflow"
Assert-FileContains -Path $loginCoordinator -Pattern "LoginAsync" -Description "login coordinator owns manual login workflow"
Assert-FileContains -Path $loginCoordinator -Pattern "TryAutoLoginAsync" -Description "login coordinator owns auto-login workflow"
Assert-FileContains -Path $loginCoordinator -Pattern "OpenServerSettingsAsync" -Description "login coordinator owns server settings workflow"
Assert-FileContains -Path $loginCoordinator -Pattern "StoredCredentialProfileForLogin" -Description "login coordinator owns stored-credential matching"
Assert-FileContains -Path $loginCoordinator -Pattern "SaveLoginProfileAsync" -Description "login coordinator owns login profile persistence"
Assert-FileContains -Path $loginCoordinator -Pattern "EnsureLoginProfileAsync" -Description "manual login saves a profile id before connecting"
Assert-FileContains -Path $loginCoordinator -Pattern "hasTypedPassword" -Description "login coordinator saves typed passwords instead of only updating options"
Assert-FileContains -Path $loginCoordinator -Pattern "loginProfile\?\.Id" -Description "manual login uses stable profile connection id"
Assert-FileNotContains -Path $shellViewModel -Pattern "private async Task LoginAsync|private async Task TryAutoLoginAsync|private async Task OpenServerSettingsAsync|SaveLoginProfileAsync|StoredCredentialProfileForLogin" -Description "shell keeps login flow out of ShellViewModel"
Assert-FileContains -Path $loginViewModel -Pattern "value\.HasStoredCredential \|\| RememberPassword" -Description "login profile selection preserves remember-password choice for new credentials"
Assert-FileContains -Path $loginViewModel -Pattern "Password = string\.Empty" -Description "login profile switching clears typed password"
Assert-FileContains -Path $loginViewModel -Pattern "Username = value\.Username \?\? string\.Empty" -Description "login profile switching avoids carrying stale usernames"
Assert-FileContains -Path $loginView -Pattern "SyncPasswordBox" -Description "login view syncs PasswordBox after view-model password clears"
Assert-FileContains -Path $shellViewModel -Pattern "DirectoryNavigationCoordinator" -Description "shell delegates directory navigation workflow"
Assert-FileContains -Path $shellViewModel -Pattern "RemoteClipboardCoordinator" -Description "shell delegates remote clipboard workflow"
Assert-FileContains -Path $shellViewModel -Pattern "PasteRemoteClipboardAsync" -Description "shell pastes remote clipboard"
Assert-FileContains -Path $shellViewModel -Pattern "SelectedRemoteItems" -Description "shell uses multi-selection for remote clipboard commands"
Assert-FileContains -Path $directoryNavigationCoordinator -Pattern "LoadAsync" -Description "directory navigation coordinator owns directory loading"
Assert-FileContains -Path $directoryNavigationCoordinator -Pattern "CurrentShare" -Description "directory navigation coordinator tracks current share"
Assert-FileContains -Path $directoryNavigationCoordinator -Pattern "CurrentPath" -Description "directory navigation coordinator tracks current path"
Assert-FileContains -Path $directoryNavigationCoordinator -Pattern "ShowShareRoot" -Description "directory navigation coordinator owns virtual share-root display"
Assert-FileContains -Path $directoryNavigationCoordinator -Pattern "目录正在加载" -Description "directory navigation coordinator reports duplicate in-flight loads"
Assert-FileContains -Path $directoryNavigationCoordinator -Pattern "CurrentNavigationNode" -Description "directory navigation coordinator resolves current tree node"
Assert-FileContains -Path $directoryNavigationCoordinator -Pattern "NormalizeDirectoryPath" -Description "directory navigation coordinator normalizes directory paths"
Assert-FileContains -Path $directoryNavigationCoordinator -Pattern "directoryLoadGeneration" -Description "directory navigation coordinator guards against stale load completion"
Assert-FileContains -Path $directoryNavigationCoordinator -Pattern "CancelActiveDirectoryLoad" -Description "directory navigation coordinator cancels in-flight load when switching roots"
Assert-FileContains -Path $directoryNavigationCoordinator -Pattern "handleSessionIssueAsync" -Description "directory navigation coordinator routes reconnectable failures to shell"
Assert-FileContains -Path $directoryService -Pattern "DirectoryListTimeout" -Description "directory service bounds SMB list waits"
Assert-FileContains -Path $directoryService -Pattern "Task\.WhenAny" -Description "directory service returns on list timeout instead of waiting forever"
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
Assert-FileContains -Path $shellViewModel -Pattern "_directoryNavigationCoordinator\.ShowShareRoot" -Description "shell routes virtual share-root display through directory coordinator"
Assert-FileContains -Path $shellViewModel -Pattern "selected\.IsShareRoot" -Description "shell opens share-root entries as SMB share roots"
Assert-FileContains -Path $shellViewModel -Pattern "HasWritableSelection" -Description "shell excludes share roots from remote cut/copy commands"
Assert-FileContains -Path $shellViewModel -Pattern "HasSingleWritableSelection" -Description "shell excludes share roots from rename/delete commands"
Assert-FileContains -Path $shellViewModel -Pattern "CanRefreshCurrentView" -Description "shell can refresh the virtual share-root view"
Assert-FileContains -Path $shellViewModel -Pattern "GoUpDirectoryAsync" -Description "shell can navigate to the parent directory"
Assert-FileContains -Path $shellViewModel -Pattern "CanGoUpDirectory" -Description "shell disables parent navigation outside remote directories"
Assert-FileContains -Path $shellViewModel -Pattern "ParentPath\(currentPath\)" -Description "shell parent navigation uses normalized parent paths"
Assert-FileContains -Path $shellViewModel -Pattern "LoadFavoritesAsync" -Description "shell loads favorites after login"
Assert-FileContains -Path $shellViewModel -Pattern "AddSelectedFavoriteAsync" -Description "shell can add current item to favorites"
Assert-FileContains -Path $shellViewModel -Pattern "OpenFavoriteAsync" -Description "shell can open favorite links"
Assert-FileContains -Path $shellViewModel -Pattern "RemoveFavoriteAsync" -Description "shell can remove favorites"
Assert-FileContains -Path $quickLinkService -Pattern "GenerateLink" -Description "favorite creation persists generated quick links"
Assert-FileContains -Path $quickLinkService -Pattern "ListQuickLinks" -Description "favorite service lists stored quick links"
Assert-FileContains -Path $quickLinkService -Pattern "DeleteQuickLink" -Description "favorite service deletes stored quick links"
Assert-FileContains -Path $quickLinkService -Pattern "BuildLink" -Description "copy-link still builds non-persisted share links"
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
Assert-FileContains -Path $fileDragDropCoordinator -Pattern "handleOperationResultAsync" -Description "drag/drop coordinator routes reconnectable operation results"
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
Assert-FileContains -Path $previewCoordinator -Pattern "CancellationTokenSource" -Description "preview coordinator cancels stale preview work"
Assert-FileContains -Path $previewCoordinator -Pattern "handleSessionIssueAsync" -Description "preview coordinator routes reconnectable failures to shell"

Assert-FileContains -Path $fileListView -Pattern "PreviewMouseMove" -Description "file list starts drag from pointer movement"
Assert-FileContains -Path $fileListXaml -Pattern "SelectionMode=`"Extended`"" -Description "file list supports extended selection"
Assert-FileContains -Path $fileListView -Pattern "RemoteDragPayload" -Description "file list recognizes internal remote drag payloads"
Assert-FileContains -Path $fileListView -Pattern "TryGetDirectoryDropTarget" -Description "file list resolves directory drop targets"
Assert-FileContains -Path $fileListView -Pattern "DataFormats\.FileDrop" -Description "file list accepts local file drops"
Assert-FileContains -Path $fileListView -Pattern "e\.Data\.GetDataPresent\(RemoteDragPayload\.DataFormat\)" -Description "file list does not re-upload internal drag-out FileDrop payloads"
Assert-FileContains -Path $fileListXaml -Pattern "x:Name=`"FilesListView`"" -Description "file list exposes named list for keyboard focus management"
Assert-FileContains -Path $mainWindowXaml -Pattern "x:Name=`"WorkspaceSearchBox`"" -Description "workspace header owns search box"
Assert-FileContains -Path $mainWindowXaml -Pattern "KeyDown=`"WorkspaceSearchBox_OnKeyDown`"" -Description "workspace header search handles escape key"
Assert-FileContains -Path $mainWindowXaml -Pattern "FileList\.BreadcrumbText" -Description "workspace header shows macOS-style breadcrumb"
Assert-FileContains -Path $mainWindowXaml -Pattern "FileList\.LocationTitle" -Description "workspace breadcrumb keeps full-location tooltip"
Assert-FileContains -Path $mainWindowXaml -Pattern "FileList\.GoUpCommand" -Description "workspace header binds parent-directory command"
Assert-FileContains -Path $mainWindowXaml -Pattern "FileList\.GoShareRootCommand" -Description "workspace header binds share-root command"
Assert-FileContains -Path $mainWindowXaml -Pattern "FileList\.RefreshCommand" -Description "workspace header binds refresh command"
Assert-FileContains -Path $mainWindowXaml -Pattern "Preview\.ToggleCommand" -Description "workspace header keeps preview toggle visible"
Assert-FileNotContains -Path $mainWindowXaml -Pattern "CreateFolderCommand" -Description "workspace header leaves create-folder out of the macOS-style toolbar"
Assert-FileContains -Path $mainWindowXaml -Pattern "HeaderUserMenuButton" -Description "workspace header exposes a macOS-style user menu"
Assert-FileContains -Path $mainWindowXaml -Pattern "LogoutCommand" -Description "workspace user menu exposes logout"
Assert-FileContains -Path $fileListXaml -Pattern "FileList\.CopyLinkCommand" -Description "file list context menu commands bind through shell data context"
Assert-FileContains -Path $fileListXaml -Pattern "FileList\.CreateFolderCommand" -Description "file list context menu exposes create-folder action"
Assert-FileNotContains -Path $rynatTheme -Pattern "ControlTemplate TargetType=`"ListViewItem`"" -Description "file list item styling preserves GridView column rendering"
Assert-FileContains -Path $fileListViewModel -Pattern "DirectoryLocationTitle" -Description "file list builds a macOS-style location title"
Assert-FileContains -Path $fileListViewModel -Pattern "DirectoryBreadcrumbText" -Description "file list builds a macOS-style breadcrumb"
Assert-FileContains -Path $fileListView -Pattern "FocusWorkspaceSearch" -Description "file list Ctrl+F focuses workspace search"
Assert-FileContains -Path $fileListView -Pattern "Key\.A" -Description "file list handles select-all shortcut"
Assert-FileContains -Path $fileListView -Pattern "SelectAll\(\)" -Description "file list select-all uses WPF selection"
Assert-FileContains -Path $fileListView -Pattern "Key\.Escape" -Description "file list handles escape key"
Assert-FileContains -Path $fileListView -Pattern "ClearSearchOrSelection" -Description "file list escape clears search or selection"
Assert-FileContains -Path $fileListView -Pattern "Key\.Delete" -Description "file list handles delete key"
Assert-FileContains -Path $fileListView -Pattern "Key\.F2" -Description "file list handles rename shortcut"
Assert-FileContains -Path $fileListView -Pattern "Key\.F5" -Description "file list handles refresh shortcut"
Assert-FileContains -Path $fileListView -Pattern "Key\.Back" -Description "file list handles Backspace parent navigation"
Assert-FileContains -Path $fileListView -Pattern "ModifierKeys\.Alt" -Description "file list handles Alt-modified shortcuts"
Assert-FileContains -Path $fileListView -Pattern "Key\.Up" -Description "file list handles Alt+Up parent navigation"
Assert-FileContains -Path $fileListView -Pattern "SystemKey" -Description "file list handles WPF system-key Alt+Up events"
Assert-FileContains -Path $fileListView -Pattern "Key\.X" -Description "file list handles cut shortcut"
Assert-FileContains -Path $fileListView -Pattern "Key\.C" -Description "file list handles remote copy shortcut"
Assert-FileContains -Path $fileListView -Pattern "Key\.V" -Description "file list handles paste shortcut"
Assert-FileContains -Path $fileListViewModel -Pattern "ShowShareRoot" -Description "file list can show virtual share root entries"
Assert-FileContains -Path $fileListViewModel -Pattern "IsShareRootView" -Description "file list tracks virtual share-root view"
Assert-FileContains -Path $fileListViewModel -Pattern "IsShareRoot: true" -Description "file list marks share root entries"
Assert-FileContains -Path $fileListViewModel -Pattern "GoUpCommand" -Description "file list exposes parent-directory command"
Assert-FileContains -Path $fileListViewModel -Pattern "GoShareRootCommand" -Description "file list exposes share-root command"
Assert-FileContains -Path $fileListViewModel -Pattern "HasWritableSelection" -Description "file list distinguishes writable selections"
Assert-FileContains -Path $fileDragDropCoordinator -Pattern "selectedItems\.Any\(selected => selected\.IsShareRoot\)" -Description "drag source excludes share-root entries"
Assert-FileContains -Path $navigationNodeViewModel -Pattern "enum NavigationDropState" -Description "navigation node tracks remote drop hover state"
Assert-FileContains -Path $navigationNodeViewModel -Pattern "RemoteDropState" -Description "navigation node exposes remote drop hover state"
Assert-FileContains -Path $navigationTreeXaml -Pattern "DragLeave=`"TreeView_OnDragLeave`"" -Description "navigation tree clears drag hover on leave"
Assert-FileContains -Path $navigationTreeXaml -Pattern "NavigationDropState\.ValidTarget" -Description "navigation tree binds valid remote drop target styling"
Assert-FileContains -Path $navigationTreeXaml -Pattern "ShowFavoritesCommand" -Description "navigation sidebar exposes favorites tab"
Assert-FileContains -Path $navigationTreeXaml -Pattern "AddFavoriteCommand" -Description "navigation sidebar exposes add-favorite action"
Assert-FileContains -Path $navigationTreeXaml -Pattern "RemoveFavoriteCommand" -Description "navigation sidebar exposes remove-favorite action"
Assert-FileContains -Path $navigationTreeXaml -Pattern "ItemsSource=`"{Binding Favorites}`"" -Description "navigation sidebar lists favorites"
Assert-FileContains -Path $navigationTreeXaml -Pattern "KeyDown=`"FavoritesList_OnKeyDown`"" -Description "favorites list handles keyboard shortcuts"
Assert-FileContains -Path $navigationTreeView -Pattern "SetRemoteDropTarget" -Description "navigation tree updates remote drop hover state"
Assert-FileContains -Path $navigationTreeView -Pattern "node\.IsExpanded = !node\.IsExpanded" -Description "navigation tree double-click toggles expansion locally"
Assert-FileContains -Path $navigationTreeView -Pattern "OpenFavoriteAsync" -Description "navigation tree opens favorite rows"
Assert-FileContains -Path $navigationTreeView -Pattern "Key\.Enter" -Description "favorites list opens selected favorite with Enter"
Assert-FileContains -Path $navigationTreeView -Pattern "Key\.Delete" -Description "favorites list removes selected favorite with Delete"
Assert-FileContains -Path $navigationTreeView -Pattern "e\.Effects == DragDropEffects\.None \? null : target" -Description "navigation tree highlights only valid remote drop targets"

Assert-FileContains -Path $fileOperationInterface -Pattern "CreateDirectoryAsync" -Description "file operation interface supports create folder"
Assert-FileContains -Path $fileOperationInterface -Pattern "DeleteAsync" -Description "file operation interface supports delete"
Assert-FileContains -Path $fileOperationInterface -Pattern "RenameAsync" -Description "file operation interface supports rename"
Assert-FileContains -Path $fileOperationInterface -Pattern "UploadFilesAsync" -Description "file operation interface supports upload"
Assert-FileContains -Path $fileOperationService -Pattern "SmbTaskOperation\.CreateDirectory" -Description "file operation service tasks core create-directory"
Assert-FileContains -Path $fileOperationService -Pattern "SmbTaskOperation\.Delete" -Description "file operation service tasks core delete"
Assert-FileContains -Path $fileOperationService -Pattern "SmbTaskOperation\.Rename" -Description "file operation service tasks core rename"
Assert-FileContains -Path $fileOperationService -Pattern "SmbTaskOperation\.UploadFile" -Description "file operation service tasks core upload"
Assert-FileContains -Path $fileOperationService -Pattern "RunWriteTaskAsync" -Description "file operation service runs writes through core task API"
Assert-FileNotContains -Path $fileOperationService -Pattern "GetAwaiter\(\)\.GetResult" -Description "file operation service does not block on async task polling"
Assert-FileNotContains -Path $fileOperationService -Pattern "CopyDirectoryRecursive|DeleteRemotePathRecursive|IRemoteCopyMoveService|FileDragDropCoordinator" -Description "file operation service stays free of recursive drag/copy workflows"
Assert-FileContains -Path $remoteCopyMoveService -Pattern "SmbCopyFile" -Description "remote copy/move service calls core copy"
Assert-FileContains -Path $remoteCopyMoveService -Pattern "SmbRename" -Description "remote copy/move service calls core move"
Assert-FileContains -Path $remoteCopyMoveService -Pattern "CopyDirectoryRecursive" -Description "remote copy/move service copies directories recursively"
Assert-FileNotContains -Path $remoteCopyMoveService -Pattern "DeleteRemotePathRecursive|DeleteTargetIfExists|replace-delete" -Description "remote copy/move service avoids delete-before-replace data loss"
Assert-FileContains -Path $remoteCopyMoveService -Pattern "directory_replace_not_supported" -Description "remote directory replace is rejected instead of deleting target first"
Assert-FileContains -Path $remoteCopyMoveService -Pattern "move_replace_not_supported" -Description "remote move replace is rejected instead of deleting target first"
Assert-FileContains -Path $remoteCopyMoveService -Pattern "cross_share_move" -Description "remote copy/move service rejects cross-share move"
Assert-FileContains -Path $fileOperationService -Pattern "replaceExisting" -Description "file operation service passes upload overwrite intent"

Assert-FileNotContains -Path $fileTransferService -Pattern "LazyRemoteDownloadStream" -Description "drag-out no longer downloads lazily during DoDragDrop"
Assert-FileContains -Path $fileTransferService -Pattern "SmbTaskOperation\.CacheFile" -Description "drag-out pre-caches through core task API"
Assert-FileContains -Path $fileTransferService -Pattern "DragCache" -Description "drag-out has local drag cache path"
Assert-FileContains -Path $fileTransferService -Pattern "CleanupDirectory" -Description "drag cache cleanup is triggered"
Assert-FileContains -Path $fileTransferService -Pattern "\.part" -Description "drag-out downloads to partial file before completion"
Assert-FileContains -Path $fileTransferService -Pattern "LocalPath" -Description "drag-out payload exposes cached local file path"
Assert-FileContains -Path $shellDragDropService -Pattern "FileGroupDescriptorW" -Description "drag-out keeps virtual file descriptor fallback"
Assert-FileContains -Path $shellDragDropService -Pattern "FileContents" -Description "drag-out keeps virtual file contents fallback"
Assert-FileContains -Path $shellDragDropService -Pattern "DataFormats\.FileDrop" -Description "drag-out publishes cached local file path to Explorer"
Assert-FileContains -Path $shellDragDropService -Pattern "Preferred DropEffect" -Description "drag-out tells Explorer the preferred local drop effect"
Assert-FileContains -Path $shellDragDropService -Pattern "RemoteDragPayload\.DataFormat" -Description "drag service publishes internal remote drag payload"

Assert-FileContains -Path $previewService -Pattern "PreviewCache" -Description "preview service uses preview cache"
Assert-FileContains -Path $previewService -Pattern "SmbCacheFile" -Description "preview service caches remote files through core"
Assert-FileContains -Path $previewService -Pattern "SmbCancelOperation" -Description "preview service cancels native cache operation on preview cancellation"
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
Assert-FileContains -Path $windowsThumbnailService -Pattern "using System\.IO;" -Description "Windows thumbnail service imports file-system helpers"
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
Assert-FileContains -Path $windowsApp -Pattern "ShutdownAsync\(\)" -Description "app exit disconnects active SMB session"

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
