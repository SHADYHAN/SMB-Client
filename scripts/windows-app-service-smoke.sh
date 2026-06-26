#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

assert_file_exists() {
    local path="$1"
    local description="$2"

    if [[ ! -f "$path" ]]; then
        printf 'Missing file: %s\n' "$path" >&2
        exit 1
    fi

    printf 'OK: %s\n' "$description"
}

assert_file_contains() {
    local path="$1"
    local pattern="$2"
    local description="$3"

    assert_file_exists "$path" "$description" >/dev/null
    if ! grep -Eq "$pattern" "$path"; then
        printf 'Check failed: %s\n' "$description" >&2
        printf '  file: %s\n' "$path" >&2
        printf '  pattern: %s\n' "$pattern" >&2
        exit 1
    fi

    printf 'OK: %s\n' "$description"
}

assert_file_not_contains() {
    local path="$1"
    local pattern="$2"
    local description="$3"

    assert_file_exists "$path" "$description" >/dev/null
    if grep -Eq "$pattern" "$path"; then
        printf 'Check failed: %s\n' "$description" >&2
        printf '  file: %s\n' "$path" >&2
        printf '  forbidden pattern: %s\n' "$pattern" >&2
        exit 1
    fi

    printf 'OK: %s\n' "$description"
}

windows_project="$ROOT_DIR/apps/windows/Rynat.WindowsClient.csproj"
windows_app="$ROOT_DIR/apps/windows/App.xaml.cs"
native_methods="$ROOT_DIR/apps/windows/CoreAdapter/NativeMethods.cs"
core_bridge="$ROOT_DIR/apps/windows/CoreAdapter/RynatCoreBridge.cs"
json_context="$ROOT_DIR/apps/windows/CoreAdapter/RynatJsonContext.cs"
core_credential="$ROOT_DIR/crates/rynat-core/src/credential.rs"
shell_view_model="$ROOT_DIR/apps/windows/UI/Shell/ShellViewModel.cs"
login_coordinator="$ROOT_DIR/apps/windows/UI/Shell/LoginCoordinator.cs"
directory_navigation_coordinator="$ROOT_DIR/apps/windows/UI/Shell/DirectoryNavigationCoordinator.cs"
directory_service="$ROOT_DIR/apps/windows/Services/Directory/DirectoryService.cs"
file_drag_drop_coordinator="$ROOT_DIR/apps/windows/UI/Shell/FileDragDropCoordinator.cs"
link_activation_coordinator="$ROOT_DIR/apps/windows/UI/Shell/LinkActivationCoordinator.cs"
preview_coordinator="$ROOT_DIR/apps/windows/UI/Shell/PreviewCoordinator.cs"
remote_clipboard_coordinator="$ROOT_DIR/apps/windows/UI/Shell/RemoteClipboardCoordinator.cs"
main_window_xaml="$ROOT_DIR/apps/windows/MainWindow.xaml"
main_window="$ROOT_DIR/apps/windows/MainWindow.xaml.cs"
login_view_model="$ROOT_DIR/apps/windows/UI/Login/LoginViewModel.cs"
file_item_view_model="$ROOT_DIR/apps/windows/UI/Files/FileItemViewModel.cs"
file_list_view_model="$ROOT_DIR/apps/windows/UI/Files/FileListViewModel.cs"
file_list_xaml="$ROOT_DIR/apps/windows/UI/Files/FileListView.xaml"
file_list_view="$ROOT_DIR/apps/windows/UI/Files/FileListView.xaml.cs"
navigation_node_view_model="$ROOT_DIR/apps/windows/UI/Navigation/NavigationNodeViewModel.cs"
navigation_tree_xaml="$ROOT_DIR/apps/windows/UI/Navigation/NavigationTreeView.xaml"
navigation_tree_view="$ROOT_DIR/apps/windows/UI/Navigation/NavigationTreeView.xaml.cs"
preview_pane_view="$ROOT_DIR/apps/windows/UI/Preview/PreviewPaneView.xaml"
preview_pane_view_model="$ROOT_DIR/apps/windows/UI/Preview/PreviewPaneViewModel.cs"
file_operation_interface="$ROOT_DIR/apps/windows/Services/FileOperations/IFileOperationService.cs"
file_operation_service="$ROOT_DIR/apps/windows/Services/FileOperations/FileOperationService.cs"
remote_copy_move_service="$ROOT_DIR/apps/windows/Services/FileOperations/RemoteCopyMoveService.cs"
file_transfer_service="$ROOT_DIR/apps/windows/Services/FileTransfers/FileTransferService.cs"
cache_cleanup_service="$ROOT_DIR/apps/windows/Services/Cache/WindowsCacheCleanupService.cs"
preview_service="$ROOT_DIR/apps/windows/Services/Preview/PreviewService.cs"
thumbnail_service_interface="$ROOT_DIR/apps/windows/Services/Preview/IThumbnailService.cs"
quick_link_service="$ROOT_DIR/apps/windows/Services/Links/QuickLinkService.cs"
link_activation_service="$ROOT_DIR/apps/windows/Services/LinkActivation/LinkActivationService.cs"
local_redirect_service="$ROOT_DIR/apps/windows/Platform/Activation/LocalLinkRedirectService.cs"
single_instance_service="$ROOT_DIR/apps/windows/Platform/Activation/WindowsSingleInstanceService.cs"
protocol_registration_service="$ROOT_DIR/apps/windows/Platform/Activation/WindowsProtocolRegistrationService.cs"
shell_drag_drop_service="$ROOT_DIR/apps/windows/Platform/Shell/WindowsShellDragDropService.cs"
windows_thumbnail_service="$ROOT_DIR/apps/windows/Platform/Shell/WindowsThumbnailService.cs"
windows_clipboard_service="$ROOT_DIR/apps/windows/Platform/Clipboard/WindowsClipboardService.cs"

printf 'Checking bridge/header/Swift/C# ABI surface...\n'
"$ROOT_DIR/scripts/check-bridge-surface.sh"

printf 'Checking WPF Windows client structure...\n'

required_files=(
    "apps/windows/Rynat.WindowsClient.csproj"
    "apps/windows/App.xaml.cs"
    "apps/windows/MainWindow.xaml"
    "apps/windows/MainWindow.xaml.cs"
    "apps/windows/CoreAdapter/NativeMethods.cs"
    "apps/windows/CoreAdapter/RynatCoreBridge.cs"
    "apps/windows/CoreAdapter/RynatCoreRequests.cs"
    "apps/windows/CoreAdapter/RynatCoreModels.cs"
    "apps/windows/CoreAdapter/RynatJsonContext.cs"
    "apps/windows/Domain/FavoriteLinkItem.cs"
    "apps/windows/Domain/RemoteFileItem.cs"
    "apps/windows/Domain/RemoteDragPayload.cs"
    "apps/windows/Domain/RemoteClipboardItem.cs"
    "apps/windows/Domain/RemoteClipboardMode.cs"
    "apps/windows/Domain/ServerSession.cs"
    "apps/windows/Services/Bootstrap/BootstrapService.cs"
    "apps/windows/Services/Directory/DirectoryService.cs"
    "apps/windows/Services/FileOperations/FileOperationService.cs"
    "apps/windows/Services/FileOperations/IRemoteCopyMoveService.cs"
    "apps/windows/Services/FileOperations/RemoteCopyMoveService.cs"
    "apps/windows/Services/Cache/WindowsCacheCleanupService.cs"
    "apps/windows/Services/FileTransfers/FileTransferService.cs"
    "apps/windows/Services/LinkActivation/LinkActivationService.cs"
    "apps/windows/Services/Links/QuickLinkService.cs"
    "apps/windows/Services/Preview/PreviewService.cs"
    "apps/windows/Services/Preview/IThumbnailService.cs"
    "apps/windows/Services/Profiles/ServerProfileService.cs"
    "apps/windows/Services/Smb/SmbSessionService.cs"
    "apps/windows/Platform/Activation/LocalLinkRedirectService.cs"
    "apps/windows/Platform/Activation/WindowsProtocolRegistrationService.cs"
    "apps/windows/Platform/Activation/WindowsSingleInstanceService.cs"
    "apps/windows/Platform/Clipboard/WindowsClipboardService.cs"
    "apps/windows/Platform/Dialogs/WindowsUserDialogService.cs"
    "apps/windows/Platform/Shell/WindowsShellDragDropService.cs"
    "apps/windows/Platform/Shell/WindowsThumbnailService.cs"
    "apps/windows/Platform/Shell/WindowsWindowForegroundService.cs"
    "apps/windows/UI/Shell/ShellViewModel.cs"
    "apps/windows/UI/Shell/LoginCoordinator.cs"
    "apps/windows/UI/Shell/DirectoryNavigationCoordinator.cs"
    "apps/windows/UI/Shell/FileDragDropCoordinator.cs"
    "apps/windows/UI/Shell/LinkActivationCoordinator.cs"
    "apps/windows/UI/Shell/PreviewCoordinator.cs"
    "apps/windows/UI/Shell/RemoteClipboardCoordinator.cs"
    "apps/windows/UI/Shell/RemoteClipboardPasteResult.cs"
    "apps/windows/UI/Files/FileItemViewModel.cs"
    "apps/windows/UI/Files/FileListView.xaml"
    "apps/windows/UI/Files/FileListView.xaml.cs"
    "apps/windows/UI/Navigation/FavoriteLinkViewModel.cs"
    "apps/windows/UI/Navigation/NavigationSidebarTab.cs"
    "apps/windows/UI/Navigation/NavigationTreeView.xaml"
    "apps/windows/UI/Preview/PreviewPaneView.xaml"
    "apps/windows/UI/Status/StatusBarView.xaml"
)

for relative_path in "${required_files[@]}"; do
    assert_file_exists "$ROOT_DIR/$relative_path" "$relative_path"
done

printf 'Checking service registration and app boundaries...\n'

assert_file_contains "$windows_project" '<UseWPF>true</UseWPF>' 'Windows client is WPF'
assert_file_contains "$windows_project" 'cargo build -p rynat-core' 'Windows build invokes Rust core build'
assert_file_contains "$windows_project" 'rynat_core\.dll' 'Windows build copies rynat_core.dll'

assert_file_contains "$windows_app" 'new RynatCoreBridge\(\)' 'App creates one core bridge'
assert_file_contains "$windows_app" 'new BootstrapService\(bridge\)' 'BootstrapService registered'
assert_file_contains "$windows_app" 'new SmbSessionService\(bridge\)' 'SmbSessionService registered'
assert_file_contains "$windows_app" 'new DirectoryService\(bridge\)' 'DirectoryService registered'
assert_file_contains "$windows_app" 'new RemoteCopyMoveService\(bridge\)' 'RemoteCopyMoveService registered'
assert_file_contains "$windows_app" 'new FileOperationService\(bridge\)' 'FileOperationService registered'
assert_file_contains "$windows_app" 'new FileTransferService\(bridge\)' 'FileTransferService registered'
assert_file_contains "$windows_app" 'new QuickLinkService\(bridge\)' 'QuickLinkService registered'
assert_file_contains "$windows_app" 'new LinkActivationService\(bridge\)' 'LinkActivationService registered'
assert_file_contains "$windows_app" 'new WindowsThumbnailService\(\)' 'Windows thumbnail service registered'
assert_file_contains "$windows_app" 'new PreviewService\(bridge, thumbnailService\)' 'PreviewService registered'
assert_file_contains "$windows_app" 'new ServerProfileService\(bridge\)' 'ServerProfileService registered'
assert_file_contains "$windows_app" 'new LocalLinkRedirectService\(bridge\)' 'local HTTP redirect service registered'
assert_file_contains "$windows_app" 'WindowsSingleInstanceService' 'single-instance service registered'
assert_file_contains "$windows_app" 'WindowsWindowForegroundService' 'foreground activation adapter registered'
assert_file_contains "$main_window" 'ShellViewModel' 'MainWindow depends only on shell view model'
assert_file_contains "$main_window" 'UpdatePreviewColumn' 'MainWindow only owns preview column view sizing'
assert_file_not_contains "$main_window" 'Services\.|Platform\.|RynatCoreBridge|Smb|DirectoryService|FileOperation' 'MainWindow stays free of service/platform logic'

printf 'Checking bridge coverage...\n'

assert_file_contains "$native_methods" 'rynat_smb_copy_file_json' 'NativeMethods exposes remote copy'
assert_file_contains "$native_methods" 'rynat_smb_start_task_json' 'NativeMethods exposes task API'
assert_file_contains "$core_bridge" 'public SmbWriteResult SmbCopyFile' 'C# bridge wraps remote copy'
assert_file_contains "$core_bridge" 'public SmbTaskStartResult SmbStartTask' 'C# bridge wraps task start'
assert_file_contains "$json_context" 'JsonSerializable\(typeof\(SmbCopyFileRequest\)\)' 'JSON source generation covers copy request'
assert_file_contains "$json_context" 'JsonSerializable\(typeof\(BridgeResponse<SmbTaskStatus>\)\)' 'JSON source generation covers task status'
assert_file_contains "$core_credential" 'CREATE_NO_WINDOW' 'Windows credential helper commands run without a visible console'

printf 'Checking WPF feature plumbing...\n'

assert_file_contains "$shell_view_model" 'CopyLinkCommand' 'shell exposes copy-link command'
assert_file_contains "$shell_view_model" 'CutCommand' 'shell exposes cut command'
assert_file_contains "$shell_view_model" 'CopyFileCommand' 'shell exposes remote copy command'
assert_file_contains "$shell_view_model" 'PasteCommand' 'shell exposes remote paste command'
assert_file_contains "$shell_view_model" 'GoUpCommand' 'shell exposes parent-directory command'
assert_file_contains "$shell_view_model" 'GoShareRootCommand' 'shell exposes share-root command'
assert_file_contains "$shell_view_model" 'LoginCoordinator' 'shell delegates login workflow'
assert_file_contains "$login_coordinator" 'LoginAsync' 'login coordinator owns manual login workflow'
assert_file_contains "$login_coordinator" 'TryAutoLoginAsync' 'login coordinator owns auto-login workflow'
assert_file_contains "$login_coordinator" 'OpenServerSettingsAsync' 'login coordinator owns server settings workflow'
assert_file_contains "$login_coordinator" 'StoredCredentialProfileForLogin' 'login coordinator owns stored-credential matching'
assert_file_contains "$login_coordinator" 'SaveLoginProfileAsync' 'login coordinator owns login profile persistence'
assert_file_contains "$login_coordinator" 'hasTypedPassword' 'login coordinator saves typed passwords instead of only updating options'
assert_file_not_contains "$shell_view_model" 'private async Task LoginAsync|private async Task TryAutoLoginAsync|private async Task OpenServerSettingsAsync|SaveLoginProfileAsync|StoredCredentialProfileForLogin' 'shell keeps login flow out of ShellViewModel'
assert_file_contains "$login_view_model" 'value\.HasStoredCredential \|\| RememberPassword' 'login profile selection preserves remember-password choice for new credentials'
assert_file_contains "$shell_view_model" 'DirectoryNavigationCoordinator' 'shell delegates directory navigation workflow'
assert_file_contains "$shell_view_model" 'RemoteClipboardCoordinator' 'shell delegates remote clipboard workflow'
assert_file_contains "$shell_view_model" 'PasteRemoteClipboardAsync' 'shell pastes remote clipboard'
assert_file_contains "$shell_view_model" 'SelectedRemoteItems' 'shell uses multi-selection for remote clipboard commands'
assert_file_contains "$directory_navigation_coordinator" 'LoadAsync' 'directory navigation coordinator owns directory loading'
assert_file_contains "$directory_navigation_coordinator" 'CurrentShare' 'directory navigation coordinator tracks current share'
assert_file_contains "$directory_navigation_coordinator" 'CurrentPath' 'directory navigation coordinator tracks current path'
assert_file_contains "$directory_navigation_coordinator" 'ShowShareRoot' 'directory navigation coordinator owns virtual share-root display'
assert_file_contains "$directory_navigation_coordinator" '目录正在加载' 'directory navigation coordinator reports duplicate in-flight loads'
assert_file_contains "$directory_navigation_coordinator" 'CurrentNavigationNode' 'directory navigation coordinator resolves current tree node'
assert_file_contains "$directory_navigation_coordinator" 'NormalizeDirectoryPath' 'directory navigation coordinator normalizes directory paths'
assert_file_contains "$directory_service" 'DirectoryListTimeout' 'directory service bounds SMB list waits'
assert_file_contains "$directory_service" 'Task\.WhenAny' 'directory service returns on list timeout instead of waiting forever'
assert_file_contains "$remote_clipboard_coordinator" 'RemoteClipboardItem' 'clipboard coordinator tracks remote clipboard item'
assert_file_contains "$remote_clipboard_coordinator" 'foreach \(var item in clipboard\.Items\)' 'clipboard coordinator pastes multiple items'
assert_file_contains "$remote_clipboard_coordinator" 'conflictNames' 'clipboard coordinator batches overwrite conflicts'
assert_file_contains "$remote_clipboard_coordinator" 'PasteAsync' 'clipboard coordinator owns paste workflow'
assert_file_contains "$shell_view_model" 'CreateFolderCommand' 'shell exposes create-folder command'
assert_file_contains "$shell_view_model" 'DeleteCommand' 'shell exposes delete command'
assert_file_contains "$shell_view_model" 'RenameCommand' 'shell exposes rename command'
assert_file_contains "$shell_view_model" 'UploadDroppedFilesAsync' 'shell handles local file drop upload'
assert_file_contains "$shell_view_model" 'StartFileDragAsync' 'shell starts drag-out workflow'
assert_file_contains "$shell_view_model" 'GetRemoteDropEffect' 'shell exposes remote drag/drop effect resolution'
assert_file_contains "$shell_view_model" 'DropRemoteItemsAsync' 'shell exposes remote drag/drop commit workflow'
assert_file_contains "$shell_view_model" 'FileDragDropCoordinator' 'shell delegates file drag/drop workflow'
assert_file_contains "$shell_view_model" '_directoryNavigationCoordinator\.ShowShareRoot' 'shell routes virtual share-root display through directory coordinator'
assert_file_contains "$shell_view_model" 'selected\.IsShareRoot' 'shell opens share-root entries as SMB share roots'
assert_file_contains "$shell_view_model" 'HasWritableSelection' 'shell excludes share roots from remote cut/copy commands'
assert_file_contains "$shell_view_model" 'HasSingleWritableSelection' 'shell excludes share roots from rename/delete commands'
assert_file_contains "$shell_view_model" 'CanRefreshCurrentView' 'shell can refresh the virtual share-root view'
assert_file_contains "$shell_view_model" 'GoUpDirectoryAsync' 'shell can navigate to the parent directory'
assert_file_contains "$shell_view_model" 'CanGoUpDirectory' 'shell disables parent navigation outside remote directories'
assert_file_contains "$shell_view_model" 'ParentPath\(currentPath\)' 'shell parent navigation uses normalized parent paths'
assert_file_contains "$shell_view_model" 'LoadFavoritesAsync' 'shell loads favorites after login'
assert_file_contains "$shell_view_model" 'AddSelectedFavoriteAsync' 'shell can add current item to favorites'
assert_file_contains "$shell_view_model" 'OpenFavoriteAsync' 'shell can open favorite links'
assert_file_contains "$shell_view_model" 'RemoveFavoriteAsync' 'shell can remove favorites'
assert_file_contains "$quick_link_service" 'GenerateLink' 'favorite creation persists generated quick links'
assert_file_contains "$quick_link_service" 'ListQuickLinks' 'favorite service lists stored quick links'
assert_file_contains "$quick_link_service" 'DeleteQuickLink' 'favorite service deletes stored quick links'
assert_file_contains "$quick_link_service" 'BuildLink' 'copy-link still builds non-persisted share links'
assert_file_contains "$file_drag_drop_coordinator" 'StartFileDragAsync' 'file drag/drop coordinator owns drag-out workflow'
assert_file_contains "$file_drag_drop_coordinator" 'GetRemoteDropEffect' 'file drag/drop coordinator resolves internal remote drop effects'
assert_file_contains "$file_drag_drop_coordinator" 'DropRemoteItemsAsync' 'file drag/drop coordinator owns internal remote drop workflow'
assert_file_contains "$file_drag_drop_coordinator" 'MoveAsync' 'internal remote drag/drop can move remote items'
assert_file_contains "$file_drag_drop_coordinator" 'CopyAsync' 'internal remote drag/drop can copy remote items'
assert_file_contains "$file_drag_drop_coordinator" 'IsInvalidRemoteDropTarget' 'internal remote drag/drop rejects invalid targets'
assert_file_contains "$file_drag_drop_coordinator" 'UploadDroppedFilesAsync' 'file drag/drop coordinator owns drop upload workflow'
assert_file_contains "$file_drag_drop_coordinator" 'ConfirmOverwrite' 'drop upload flow confirms same-name replacement'
assert_file_contains "$file_drag_drop_coordinator" 'CreateDragDownloadPayloadAsync' 'drag-out flow creates virtual-file payload'
assert_file_contains "$file_drag_drop_coordinator" 'UploadFilesAsync' 'drop upload flow calls upload service'
assert_file_contains "$shell_view_model" 'PreviewCoordinator' 'shell delegates preview workflow'
assert_file_contains "$shell_view_model" 'ActivateExternalArgumentsAsync' 'shell accepts external activation'
assert_file_contains "$shell_view_model" 'LinkActivationCoordinator' 'shell delegates link activation workflow'
assert_file_contains "$shell_view_model" 'OpenLinkRequestAsync' 'shell opens activated links'
assert_file_contains "$shell_view_model" 'SetText\(link\.HttpUrl\)' 'Windows copy-link uses document-friendly HTTP share links'
assert_file_contains "$link_activation_coordinator" 'ActivateStartupArgumentsAsync' 'link activation coordinator parses startup arguments'
assert_file_contains "$link_activation_coordinator" 'ConsumePendingIfPossibleAsync' 'link activation coordinator owns pending activation'
assert_file_contains "$link_activation_coordinator" 'CanOpenWithSession' 'link activation coordinator checks active session'
assert_file_contains "$preview_coordinator" 'SelectFileAsync' 'preview coordinator owns selection preview flow'
assert_file_contains "$preview_coordinator" 'ShowPreviewLoading' 'preview coordinator shows loading state'
assert_file_contains "$preview_coordinator" 'ShowPreviewInfo' 'preview coordinator shows preview info'
assert_file_contains "$preview_coordinator" 'ShowPreviewUnavailable' 'preview coordinator handles preview failures'

assert_file_contains "$file_list_view" 'PreviewMouseMove' 'file list starts drag from pointer movement'
assert_file_contains "$file_list_xaml" 'SelectionMode="Extended"' 'file list supports extended selection'
assert_file_contains "$file_item_view_model" 'enum RemoteDropState' 'file item tracks remote drop hover state'
assert_file_contains "$file_item_view_model" 'ObservableObject' 'file item notifies remote drop hover state changes'
assert_file_contains "$file_list_view" 'RemoteDragPayload' 'file list recognizes internal remote drag payloads'
assert_file_contains "$file_list_view" 'TryGetDirectoryDropTarget' 'file list resolves directory drop targets'
assert_file_contains "$file_list_view" 'SetRemoteDropTarget' 'file list updates remote drop hover state'
assert_file_contains "$file_list_view" 'e\.Effects == DragDropEffects\.None \? null : target' 'file list highlights only valid remote drop targets'
assert_file_contains "$file_list_xaml" 'DragLeave="ListView_OnDragLeave"' 'file list clears drag hover on leave'
assert_file_contains "$file_list_xaml" 'RemoteDropState\.ValidTarget' 'file list binds valid remote drop target styling'
assert_file_contains "$file_list_view" 'DataFormats\.FileDrop' 'file list accepts local file drops'
assert_file_contains "$file_list_xaml" 'x:Name="FilesListView"' 'file list exposes named list for keyboard focus management'
assert_file_contains "$main_window_xaml" 'x:Name="WorkspaceSearchBox"' 'workspace header owns search box'
assert_file_contains "$main_window_xaml" 'KeyDown="WorkspaceSearchBox_OnKeyDown"' 'workspace header search handles escape key'
assert_file_contains "$main_window_xaml" 'FileList\.BreadcrumbText' 'workspace header shows macOS-style breadcrumb'
assert_file_contains "$main_window_xaml" 'FileList\.LocationTitle' 'workspace breadcrumb keeps full-location tooltip'
assert_file_contains "$main_window_xaml" 'FileList\.GoUpCommand' 'workspace header binds parent-directory command'
assert_file_contains "$main_window_xaml" 'FileList\.GoShareRootCommand' 'workspace header binds share-root command'
assert_file_contains "$main_window_xaml" 'FileList\.RefreshCommand' 'workspace header binds refresh command'
assert_file_contains "$main_window_xaml" 'Preview\.ToggleCommand' 'workspace header keeps preview toggle visible'
assert_file_contains "$file_list_xaml" 'FileList\.CopyLinkCommand' 'file list context menu commands bind through shell data context'
assert_file_contains "$file_list_view_model" 'DirectoryLocationTitle' 'file list builds a macOS-style location title'
assert_file_contains "$file_list_view_model" 'DirectoryBreadcrumbText' 'file list builds a macOS-style breadcrumb'
assert_file_contains "$file_list_view" 'FocusWorkspaceSearch' 'file list Ctrl+F focuses workspace search'
assert_file_contains "$file_list_view" 'Key\.A' 'file list handles select-all shortcut'
assert_file_contains "$file_list_view" 'SelectAll\(\)' 'file list select-all uses WPF selection'
assert_file_contains "$file_list_view" 'Key\.Escape' 'file list handles escape key'
assert_file_contains "$file_list_view" 'ClearSearchOrSelection' 'file list escape clears search or selection'
assert_file_contains "$file_list_view" 'Key\.Delete' 'file list handles delete key'
assert_file_contains "$file_list_view" 'Key\.F2' 'file list handles rename shortcut'
assert_file_contains "$file_list_view" 'Key\.F5' 'file list handles refresh shortcut'
assert_file_contains "$file_list_view" 'Key\.Back' 'file list handles Backspace parent navigation'
assert_file_contains "$file_list_view" 'ModifierKeys\.Alt' 'file list handles Alt-modified shortcuts'
assert_file_contains "$file_list_view" 'Key\.Up' 'file list handles Alt+Up parent navigation'
assert_file_contains "$file_list_view" 'SystemKey' 'file list handles WPF system-key Alt+Up events'
assert_file_contains "$file_list_view" 'Key\.X' 'file list handles cut shortcut'
assert_file_contains "$file_list_view" 'Key\.C' 'file list handles remote copy shortcut'
assert_file_contains "$file_list_view" 'Key\.V' 'file list handles paste shortcut'
assert_file_contains "$file_list_view_model" 'ShowShareRoot' 'file list can show virtual share root entries'
assert_file_contains "$file_list_view_model" 'IsShareRootView' 'file list tracks virtual share-root view'
assert_file_contains "$file_list_view_model" 'IsShareRoot: true' 'file list marks share root entries'
assert_file_contains "$file_list_view_model" 'GoUpCommand' 'file list exposes parent-directory command'
assert_file_contains "$file_list_view_model" 'GoShareRootCommand' 'file list exposes share-root command'
assert_file_contains "$file_list_view_model" 'HasWritableSelection' 'file list distinguishes writable selections'
assert_file_contains "$file_drag_drop_coordinator" 'selectedItems\.Any\(selected => selected\.IsShareRoot\)' 'drag source excludes share-root entries'
assert_file_contains "$navigation_node_view_model" 'enum NavigationDropState' 'navigation node tracks remote drop hover state'
assert_file_contains "$navigation_node_view_model" 'RemoteDropState' 'navigation node exposes remote drop hover state'
assert_file_contains "$navigation_tree_xaml" 'DragLeave="TreeView_OnDragLeave"' 'navigation tree clears drag hover on leave'
assert_file_contains "$navigation_tree_xaml" 'NavigationDropState\.ValidTarget' 'navigation tree binds valid remote drop target styling'
assert_file_contains "$navigation_tree_xaml" 'ShowFavoritesCommand' 'navigation sidebar exposes favorites tab'
assert_file_contains "$navigation_tree_xaml" 'AddFavoriteCommand' 'navigation sidebar exposes add-favorite action'
assert_file_contains "$navigation_tree_xaml" 'RemoveFavoriteCommand' 'navigation sidebar exposes remove-favorite action'
assert_file_contains "$navigation_tree_xaml" 'ItemsSource="\{Binding Favorites\}"' 'navigation sidebar lists favorites'
assert_file_contains "$navigation_tree_xaml" 'KeyDown="FavoritesList_OnKeyDown"' 'favorites list handles keyboard shortcuts'
assert_file_contains "$navigation_tree_view" 'SetRemoteDropTarget' 'navigation tree updates remote drop hover state'
assert_file_contains "$navigation_tree_view" 'node\.IsExpanded = !node\.IsExpanded' 'navigation tree double-click toggles expansion locally'
assert_file_contains "$navigation_tree_view" 'OpenFavoriteAsync' 'navigation tree opens favorite rows'
assert_file_contains "$navigation_tree_view" 'Key\.Enter' 'favorites list opens selected favorite with Enter'
assert_file_contains "$navigation_tree_view" 'Key\.Delete' 'favorites list removes selected favorite with Delete'
assert_file_contains "$navigation_tree_view" 'e\.Effects == DragDropEffects\.None \? null : target' 'navigation tree highlights only valid remote drop targets'

assert_file_contains "$file_operation_interface" 'CreateDirectoryAsync' 'file operation interface supports create folder'
assert_file_contains "$file_operation_interface" 'DeleteAsync' 'file operation interface supports delete'
assert_file_contains "$file_operation_interface" 'RenameAsync' 'file operation interface supports rename'
assert_file_contains "$file_operation_interface" 'UploadFilesAsync' 'file operation interface supports upload'
assert_file_contains "$file_operation_service" 'SmbCreateDirectory' 'file operation service calls core create-directory'
assert_file_contains "$file_operation_service" 'SmbDelete' 'file operation service calls core delete'
assert_file_contains "$file_operation_service" 'SmbRename' 'file operation service calls core rename'
assert_file_contains "$file_operation_service" 'SmbUploadFile' 'file operation service calls core upload'
assert_file_not_contains "$file_operation_service" 'CopyDirectoryRecursive|DeleteRemotePathRecursive|IRemoteCopyMoveService|FileDragDropCoordinator' 'file operation service stays free of recursive drag/copy workflows'
assert_file_contains "$remote_copy_move_service" 'SmbCopyFile' 'remote copy/move service calls core copy'
assert_file_contains "$remote_copy_move_service" 'SmbRename' 'remote copy/move service calls core move'
assert_file_contains "$remote_copy_move_service" 'CopyDirectoryRecursive' 'remote copy/move service copies directories recursively'
assert_file_contains "$remote_copy_move_service" 'DeleteRemotePathRecursive' 'remote copy/move service replaces directories recursively'
assert_file_contains "$remote_copy_move_service" 'cross_share_move' 'remote copy/move service rejects cross-share move'
assert_file_contains "$file_operation_service" 'replaceExisting' 'file operation service passes upload overwrite intent'

assert_file_contains "$file_transfer_service" 'LazyRemoteDownloadStream' 'drag-out uses lazy virtual-file download stream'
assert_file_contains "$file_transfer_service" 'DragCache' 'drag-out has local drag cache path'
assert_file_contains "$file_transfer_service" 'CleanupDirectory' 'drag cache cleanup is triggered'
assert_file_contains "$file_transfer_service" '\.part' 'drag-out downloads to partial file before completion'
assert_file_contains "$shell_drag_drop_service" 'FileGroupDescriptorW' 'drag-out publishes virtual file descriptor'
assert_file_contains "$shell_drag_drop_service" 'FileContents' 'drag-out publishes virtual file contents'
assert_file_contains "$shell_drag_drop_service" 'Preferred DropEffect' 'drag-out tells Explorer the preferred local drop effect'
assert_file_contains "$shell_drag_drop_service" 'RemoteDragPayload\.DataFormat' 'drag service publishes internal remote drag payload'

assert_file_contains "$preview_service" 'PreviewCache' 'preview service uses preview cache'
assert_file_contains "$preview_service" 'SmbCacheFile' 'preview service caches remote files through core'
assert_file_contains "$preview_service" 'CreateImageThumbnail' 'preview service generates lightweight image thumbnails'
assert_file_contains "$preview_service" 'CreateVideoPoster' 'preview service generates video poster thumbnails'
assert_file_contains "$preview_service" 'TryCreateThumbnail' 'preview service delegates video thumbnails to platform shell'
assert_file_contains "$preview_service" 'DecodePixelWidth' 'preview service decodes scaled image previews'
assert_file_contains "$preview_service" '图片较大，暂不自动缓存预览' 'preview service skips large automatic image preview caching'
assert_file_contains "$preview_service" 'InlineVideoPreviewMaxBytes' 'preview service caps inline video preview caching'
assert_file_contains "$preview_service" '暂不自动缓存预览' 'preview service skips large automatic video caching'
assert_file_contains "$preview_service" 'maxBytes' 'preview service limits preview cache bytes'
assert_file_contains "$thumbnail_service_interface" 'TryCreateThumbnail' 'thumbnail abstraction exposes platform thumbnail creation'
assert_file_contains "$windows_thumbnail_service" 'IShellItemImageFactory' 'Windows thumbnail service uses shell thumbnail extraction'
assert_file_contains "$windows_thumbnail_service" 'SHCreateItemFromParsingName' 'Windows thumbnail service creates shell items from local paths'
assert_file_contains "$windows_thumbnail_service" 'DeleteObject' 'Windows thumbnail service releases shell HBITMAP handles'
assert_file_contains "$windows_thumbnail_service" 'using System\.IO;' 'Windows thumbnail service imports file-system helpers'
assert_file_contains "$preview_pane_view" 'ShouldShowImagePreview' 'preview pane shows video poster before playback'
assert_file_contains "$preview_pane_view" 'ShouldShowVideoPreview' 'preview pane shows video control only during playback'
assert_file_contains "$preview_pane_view_model" 'IsVideoPlaying' 'preview state tracks video playback display mode'
assert_file_contains "$preview_service" 'CleanupDirectory' 'preview cache cleanup is triggered'
assert_file_contains "$cache_cleanup_service" 'PartialFileMaxAge' 'cache cleanup removes stale partial files'
assert_file_contains "$cache_cleanup_service" 'DeleteEmptyDirectories' 'cache cleanup prunes empty directories'

printf 'Checking Windows activation plumbing...\n'

assert_file_contains "$windows_app" 'StartAsync\(e\.Args\)' 'single-instance service receives startup arguments'
assert_file_contains "$windows_app" 'InitializeAsync\(e\.Args\)' 'shell receives startup arguments'
assert_file_contains "$windows_app" 'ActivateArguments\(viewModel, args\.Arguments' 'external activations route through app'
assert_file_contains "$windows_app" 'BringToFront\(activeWindow\)' 'external activation foregrounds main window'
assert_file_contains "$windows_app" 'ActivateExternalArgumentsAsync\(arguments\)' 'external activation reaches shell view model'

assert_file_contains "$link_activation_service" 'TryExtractStartupLink' 'link activation extracts startup links'
assert_file_contains "$link_activation_service" "Split\\(' '" 'link activation extracts embedded links from argument strings'
assert_file_contains "$link_activation_service" 'NormalizeRawLink' 'link activation normalizes raw links'
assert_file_contains "$link_activation_service" 'rynat://s/\?' 'link activation normalizes malformed compact deep link separator'
assert_file_contains "$link_activation_service" 'http://' 'link activation accepts local HTTP links'
assert_file_contains "$link_activation_service" 'https://' 'link activation accepts public HTTPS links'
assert_file_contains "$link_activation_service" 'CanOpenWithSession' 'link activation validates active session endpoint'

assert_file_contains "$local_redirect_service" 'IPAddress\.Loopback' 'local redirect binds to loopback only'
assert_file_contains "$local_redirect_service" 'MaxRequestLineBytes' 'local redirect caps request line size'
assert_file_contains "$local_redirect_service" 'RequestTimeout' 'local redirect uses request timeout'
assert_file_contains "$local_redirect_service" 'TryBuildDeepLink' 'local redirect builds deep link'
assert_file_contains "$local_redirect_service" 'GET' 'local redirect only accepts GET'
assert_file_contains "$local_redirect_service" 'Uri\.TryCreate' 'local redirect parses local URL safely'
assert_file_contains "$local_redirect_service" 'rynat://s' 'local redirect emits rynat deep link'
assert_file_contains "$local_redirect_service" '404 Not Found' 'local redirect rejects unsupported paths'
assert_file_contains "$local_redirect_service" 'RedirectPageRequest\(deepLink, AlreadyActivated: true\)' 'local redirect serves the already-activated close page'
assert_file_contains "$local_redirect_service" 'text/html; charset=utf-8' 'local redirect returns browser-close HTML after activation'

assert_file_contains "$single_instance_service" 'MutexName' 'single-instance uses mutex'
assert_file_contains "$single_instance_service" 'PipeName' 'single-instance uses named pipe'
assert_file_contains "$single_instance_service" 'ForwardAsync' 'secondary instance forwards activation arguments'
assert_file_contains "$single_instance_service" 'JsonSerializer\.Serialize' 'single-instance serializes forwarded arguments'
assert_file_contains "$single_instance_service" 'JsonSerializer\.Deserialize<string\[\]>' 'single-instance deserializes forwarded arguments'
assert_file_contains "$single_instance_service" 'Activated\?\.Invoke' 'single-instance raises activation event'

assert_file_contains "$protocol_registration_service" 'URL Protocol' 'protocol registration marks URL protocol'
assert_file_contains "$protocol_registration_service" 'shell\\open\\command' 'protocol registration writes open command'
assert_file_contains "$protocol_registration_service" '%1' 'protocol registration forwards original activation argument'

printf 'Windows WPF app service smoke check passed.\n'
