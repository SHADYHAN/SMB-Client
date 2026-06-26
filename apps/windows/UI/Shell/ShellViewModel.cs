using System.Windows;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Platform.Clipboard;
using Rynat.WindowsClient.Platform.Dialogs;
using Rynat.WindowsClient.Platform.Shell;
using Rynat.WindowsClient.Services.Bootstrap;
using Rynat.WindowsClient.Services.Directory;
using Rynat.WindowsClient.Services.FileOperations;
using Rynat.WindowsClient.Services.FileTransfers;
using Rynat.WindowsClient.Services.Links;
using Rynat.WindowsClient.Services.LinkActivation;
using Rynat.WindowsClient.Services.Preview;
using Rynat.WindowsClient.Services.Profiles;
using Rynat.WindowsClient.Services.Smb;
using Rynat.WindowsClient.UI.Files;
using Rynat.WindowsClient.UI.Infrastructure;
using Rynat.WindowsClient.UI.Login;
using Rynat.WindowsClient.UI.Navigation;
using Rynat.WindowsClient.UI.Preview;
using Rynat.WindowsClient.UI.Status;

namespace Rynat.WindowsClient.UI.Shell;

public sealed class ShellViewModel : ObservableObject
{
    private readonly IBootstrapService _bootstrapService;
    private readonly IFileOperationService _fileOperationService;
    private readonly IRemoteCopyMoveService _remoteCopyMoveService;
    private readonly IQuickLinkService _quickLinkService;
    private readonly IClipboardService _clipboardService;
    private readonly IUserDialogService _userDialogService;
    private ServerSession? _session;
    private readonly LoginCoordinator _loginCoordinator;
    private readonly DirectoryNavigationCoordinator _directoryNavigationCoordinator;
    private readonly FileDragDropCoordinator _fileDragDropCoordinator;
    private readonly LinkActivationCoordinator _linkActivationCoordinator;
    private readonly PreviewCoordinator _previewCoordinator;
    private readonly RemoteClipboardCoordinator _remoteClipboardCoordinator;
    private bool _isLoggedIn;

    public ShellViewModel(
        IBootstrapService bootstrapService,
        ISmbSessionService sessionService,
        IDirectoryService directoryService,
        IFileOperationService fileOperationService,
        IRemoteCopyMoveService remoteCopyMoveService,
        IFileTransferService fileTransferService,
        IQuickLinkService quickLinkService,
        ILinkActivationService linkActivationService,
        IPreviewService previewService,
        IServerProfileService serverProfileService,
        IClipboardService clipboardService,
        IUserDialogService userDialogService,
        IServerSettingsDialogService serverSettingsDialogService,
        IWindowsShellDragDropService shellDragDropService
    )
    {
        _bootstrapService = bootstrapService;
        _fileOperationService = fileOperationService;
        _remoteCopyMoveService = remoteCopyMoveService;
        _quickLinkService = quickLinkService;
        _clipboardService = clipboardService;
        _userDialogService = userDialogService;
        _loginCoordinator = new LoginCoordinator(
            sessionService,
            serverProfileService,
            serverSettingsDialogService,
            Login,
            Status,
            CompleteLoginAsync,
            UserFacingError,
            () => IsLoggedIn
        );
        _directoryNavigationCoordinator = new DirectoryNavigationCoordinator(
            directoryService,
            FileList,
            Navigation,
            Preview,
            Status,
            UserFacingError
        );
        _fileDragDropCoordinator = new FileDragDropCoordinator(
            fileTransferService,
            _fileOperationService,
            _remoteCopyMoveService,
            directoryService,
            shellDragDropService,
            _userDialogService,
            FileList,
            Status,
            UserFacingError
        );
        _linkActivationCoordinator = new LinkActivationCoordinator(linkActivationService);
        _previewCoordinator = new PreviewCoordinator(previewService, FileList, Preview);
        _remoteClipboardCoordinator = new RemoteClipboardCoordinator(
            _remoteCopyMoveService,
            names => _userDialogService.ConfirmOverwrite(names)
        );

        Login.LoginCommand = new AsyncRelayCommand(_loginCoordinator.LoginAsync, _loginCoordinator.CanLogin);
        Login.ServerSettingsCommand = new AsyncRelayCommand(_loginCoordinator.OpenServerSettingsAsync, () => !Login.IsBusy);
        FileList.OpenItemCommand = new AsyncRelayCommand(OpenSelectedItemAsync, () => FileList.HasSingleSelection);
        FileList.CopyLinkCommand = new AsyncRelayCommand(CopySelectedFileLinkAsync, () => FileList.HasSingleSelection && _session is not null);
        FileList.CutCommand = new RelayCommand(CutSelectedItems, () => FileList.HasWritableSelection && _session is not null);
        FileList.CopyFileCommand = new RelayCommand(CopySelectedItems, () => FileList.HasWritableSelection && _session is not null);
        FileList.PasteCommand = new AsyncRelayCommand(PasteRemoteClipboardAsync, CanPasteRemoteClipboard);
        FileList.RefreshCommand = new AsyncRelayCommand(RefreshCurrentDirectoryAsync, CanRefreshCurrentView);
        FileList.GoUpCommand = new AsyncRelayCommand(GoUpDirectoryAsync, CanGoUpDirectory);
        FileList.CreateFolderCommand = new AsyncRelayCommand(CreateFolderAsync, CanUseCurrentDirectory);
        FileList.DeleteCommand = new AsyncRelayCommand(DeleteSelectedItemAsync, () => FileList.HasSingleWritableSelection && _session is not null);
        FileList.RenameCommand = new AsyncRelayCommand(RenameSelectedItemAsync, () => FileList.HasSingleWritableSelection && _session is not null);
        Navigation.ShowSharesCommand = new RelayCommand(Navigation.ShowShares);
        Navigation.ShowFavoritesCommand = new RelayCommand(Navigation.ShowFavorites);
        Navigation.AddFavoriteCommand = new AsyncRelayCommand(AddSelectedFavoriteAsync, () => FileList.HasSingleSelection && _session is not null);
        Navigation.RemoveFavoriteCommand = new AsyncRelayCommand(RemoveFavoriteAsync, parameter => parameter is FavoriteLinkViewModel);
        Preview.ToggleCommand = new RelayCommand(() => Preview.IsVisible = !Preview.IsVisible);
        Preview.CopyLinkCommand = new AsyncRelayCommand(CopyPreviewLinkAsync, () => Preview.SelectedItem is not null && _session is not null);
    }

    public LoginViewModel Login { get; } = new();

    public NavigationTreeViewModel Navigation { get; } = new();

    public FileListViewModel FileList { get; } = new();

    public PreviewPaneViewModel Preview { get; } = new();

    public StatusBarViewModel Status { get; } = new();

    public bool IsLoggedIn
    {
        get => _isLoggedIn;
        private set => SetProperty(ref _isLoggedIn, value);
    }

    public async Task InitializeAsync(IReadOnlyList<string>? startupArguments = null)
    {
        try
        {
            var state = await _bootstrapService.LoadAsync();
            Login.LoadServerProfiles(
                state.ServerProfiles,
                state.ActiveServer,
                state.ActiveUsername,
                state.RememberPassword,
                state.AutoLogin
            );

            Status.Message = state.ServerProfiles.Count == 0
                ? "未找到已保存服务器，已填入默认服务器地址。"
                : $"已加载 {state.ServerProfiles.Count} 个服务器配置。";

            await _loginCoordinator.TryAutoLoginAsync();
            await ActivateStartupLinkAsync(startupArguments);
        }
        catch (Exception ex)
        {
            Status.Message = UserFacingError(ex, "启动信息加载失败");
        }
    }

    public async Task SelectNavigationNodeAsync(NavigationNodeViewModel node)
    {
        if (_session is null)
        {
            return;
        }

        await LoadDirectoryAsync(node.Share, node.Path, node, expandNavigationNode: null);
    }

    public void ReportUiError(Exception exception, string fallback)
    {
        Status.Message = UserFacingError(exception, fallback);
    }

    public async Task ActivateExternalArgumentsAsync(IReadOnlyList<string> rawArguments)
    {
        await ActivateStartupLinkAsync(rawArguments);
    }

    public async Task ToggleNavigationNodeAsync(NavigationNodeViewModel node)
    {
        if (_session is null)
        {
            return;
        }

        var shouldExpand = !node.IsExpanded;
        var loaded = await LoadDirectoryAsync(node.Share, node.Path, node, expandNavigationNode: shouldExpand);
        if (!loaded)
        {
            node.IsExpanded = shouldExpand;
        }
    }

    public async Task SelectFileAsync(FileItemViewModel? item)
    {
        await _previewCoordinator.SelectFileAsync(_session, item, RefreshFileCommands);
    }

    public async Task StartFileDragAsync(
        object dragSource,
        FileItemViewModel? item,
        IReadOnlyList<RemoteFileItem>? preservedSelection = null
    )
    {
        await _fileDragDropCoordinator.StartFileDragAsync(
            _session,
            dragSource,
            item,
            preservedSelection
        );
    }

    public async Task UploadDroppedFilesAsync(IReadOnlyList<string> localPaths)
    {
        await _fileDragDropCoordinator.UploadDroppedFilesAsync(
            _session,
            _directoryNavigationCoordinator.CurrentShare,
            _directoryNavigationCoordinator.CurrentPath,
            localPaths,
            RefreshCurrentDirectoryAsync
        );
    }

    public DragDropEffects GetRemoteDropEffect(
        RemoteDragPayload? payload,
        string targetShare,
        string targetDirectory,
        bool copyRequested
    )
    {
        return _fileDragDropCoordinator.GetRemoteDropEffect(
            payload,
            targetShare,
            targetDirectory,
            copyRequested
        );
    }

    public async Task DropRemoteItemsAsync(
        RemoteDragPayload payload,
        string targetShare,
        string targetDirectory,
        bool copyRequested
    )
    {
        await _fileDragDropCoordinator.DropRemoteItemsAsync(
            _session,
            payload,
            targetShare,
            targetDirectory,
            copyRequested,
            RefreshCurrentDirectoryAsync
        );
    }

    public async Task OpenFavoriteAsync(FavoriteLinkViewModel favorite)
    {
        if (_session is null)
        {
            return;
        }

        if (!FavoriteMatchesSession(favorite.Item, _session))
        {
            Status.Message = "收藏属于其他服务器。";
            return;
        }

        var directoryPath = favorite.Item.IsDirectory
            ? favorite.Item.Path
            : ParentPath(favorite.Item.Path);
        var request = new LinkOpenRequest(
            favorite.Item.ServerHost,
            favorite.Item.Share,
            directoryPath,
            favorite.Item.IsDirectory ? null : favorite.Item.Path,
            !favorite.Item.IsDirectory
        );

        Status.Message = await OpenLinkRequestAsync(request)
            ? "已打开收藏。"
            : "收藏打开失败。";
    }

    private async Task CompleteLoginAsync(ServerSession session)
    {
        _session = session;
        _remoteClipboardCoordinator.Clear();
        Navigation.LoadShares(_session.Shares);
        IsLoggedIn = true;
        Login.Password = "";
        _directoryNavigationCoordinator.ShowShareRoot(
            _session,
            $"已显示 {_session.Shares.Count} 个共享。",
            RefreshFileCommands
        );
        await LoadFavoritesAsync();
        Status.Message = $"已连接 {_session.Host}。";
        await ConsumePendingLinkIfPossibleAsync();
    }

    private async Task CopySelectedFileLinkAsync()
    {
        await CopyLinkAsync(FileList.SelectedItem?.Item);
    }

    private async Task CopyPreviewLinkAsync()
    {
        await CopyLinkAsync(Preview.SelectedItem);
    }

    private async Task AddSelectedFavoriteAsync()
    {
        if (_session is null || FileList.SelectedItem?.Item is not { } item)
        {
            return;
        }

        try
        {
            Status.Message = "正在添加收藏...";
            var favorite = await _quickLinkService.AddFavoriteAsync(_session, item);
            Navigation.UpsertFavorite(favorite);
            Navigation.ShowFavorites();
            Status.Message = $"已收藏 {favorite.Name}。";
        }
        catch (Exception ex)
        {
            Status.Message = UserFacingError(ex, "收藏失败");
        }
    }

    private async Task RemoveFavoriteAsync(object? parameter)
    {
        if (parameter is not FavoriteLinkViewModel favorite)
        {
            return;
        }

        try
        {
            await _quickLinkService.DeleteFavoriteAsync(favorite.Item.Id);
            Navigation.RemoveFavorite(favorite.Item.Id);
            Status.Message = "已移除收藏。";
        }
        catch (Exception ex)
        {
            Status.Message = UserFacingError(ex, "移除收藏失败");
        }
    }

    private async Task LoadFavoritesAsync()
    {
        if (_session is null)
        {
            Navigation.LoadFavorites(Array.Empty<FavoriteLinkItem>());
            return;
        }

        try
        {
            var favorites = await _quickLinkService.ListFavoritesAsync();
            Navigation.LoadFavorites(favorites.Where(favorite => FavoriteMatchesSession(favorite, _session)));
        }
        catch (Exception ex)
        {
            Navigation.LoadFavorites(Array.Empty<FavoriteLinkItem>());
            Status.Message = UserFacingError(ex, "收藏加载失败");
        }
    }

    private async Task ActivateStartupLinkAsync(IReadOnlyList<string>? rawArguments)
    {
        var statusMessage = await _linkActivationCoordinator.ActivateStartupArgumentsAsync(
            rawArguments,
            _session,
            OpenLinkRequestAsync
        );
        if (statusMessage is not null)
        {
            Status.Message = statusMessage;
        }
    }

    private async Task ConsumePendingLinkIfPossibleAsync()
    {
        var statusMessage = await _linkActivationCoordinator.ConsumePendingIfPossibleAsync(
            _session,
            OpenLinkRequestAsync
        );
        if (statusMessage is not null)
        {
            Status.Message = statusMessage;
        }
    }

    private async Task<bool> OpenLinkRequestAsync(LinkOpenRequest request)
    {
        var opened = await LoadDirectoryAsync(request.Share, request.DirectoryPath, null, expandNavigationNode: false);
        if (!opened)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.SelectedPath))
        {
            var item = FileList.Items.FirstOrDefault(candidate =>
                string.Equals(
                    DirectoryNavigationCoordinator.NormalizeDirectoryPath(candidate.Item.Path),
                    DirectoryNavigationCoordinator.NormalizeDirectoryPath(request.SelectedPath),
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (item is not null)
            {
                FileList.SelectedItem = item;
                await SelectFileAsync(item);
            }
        }

        return true;
    }

    private async Task CopyLinkAsync(RemoteFileItem? item)
    {
        if (_session is null || item is null)
        {
            return;
        }

        try
        {
            Status.Message = "正在生成链接...";
            var link = await _quickLinkService.BuildAsync(_session, item);
            _clipboardService.SetText(link.HttpUrl);
            Status.Message = "分享链接已复制。";
        }
        catch (Exception ex)
        {
            Status.Message = UserFacingError(ex, "链接复制失败");
        }
    }

    private async Task RefreshCurrentDirectoryAsync()
    {
        if (_session is not null && FileList.IsShareRootView)
        {
            _directoryNavigationCoordinator.ShowShareRoot(
                _session,
                $"已刷新，共 {_session.Shares.Count} 个共享。",
                RefreshFileCommands
            );
            return;
        }

        await _directoryNavigationCoordinator.RefreshAsync(_session, RefreshFileCommands);
    }

    private async Task CreateFolderAsync()
    {
        var currentShare = _directoryNavigationCoordinator.CurrentShare;
        if (_session is null || currentShare is null)
        {
            return;
        }

        var name = _userDialogService.PromptText("新建文件夹", "文件夹名称", "新建文件夹");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var result = await _fileOperationService.CreateDirectoryAsync(
            _session,
            currentShare,
            _directoryNavigationCoordinator.CurrentPath,
            name
        );
        Status.Message = result.Summary;
        if (result.Succeeded)
        {
            await RefreshCurrentDirectoryAsync();
        }
    }

    private async Task DeleteSelectedItemAsync()
    {
        if (_session is null || FileList.SelectedItem?.Item is not { } item)
        {
            return;
        }

        if (!_userDialogService.Confirm("删除", $"确定删除 {item.Name} 吗？"))
        {
            return;
        }

        var result = await _fileOperationService.DeleteAsync(_session, item);
        Status.Message = result.Summary;
        if (result.Succeeded)
        {
            await RefreshCurrentDirectoryAsync();
        }
    }

    private async Task RenameSelectedItemAsync()
    {
        if (_session is null || FileList.SelectedItem?.Item is not { } item)
        {
            return;
        }

        var name = _userDialogService.PromptText("重命名", "名称", item.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var result = await _fileOperationService.RenameAsync(_session, item, name);
        Status.Message = result.Summary;
        if (result.Succeeded)
        {
            await RefreshCurrentDirectoryAsync();
        }
    }

    private void CutSelectedItems()
    {
        var items = FileList.SelectedRemoteItems;
        if (items.Count == 0)
        {
            return;
        }

        Status.Message = _remoteClipboardCoordinator.Cut(items);
        RefreshFileCommands();
    }

    private void CopySelectedItems()
    {
        var items = FileList.SelectedRemoteItems;
        if (items.Count == 0)
        {
            return;
        }

        Status.Message = _remoteClipboardCoordinator.Copy(items);
        RefreshFileCommands();
    }

    private async Task PasteRemoteClipboardAsync()
    {
        var currentShare = _directoryNavigationCoordinator.CurrentShare;
        if (_session is null || currentShare is null)
        {
            return;
        }

        try
        {
            Status.Message = "正在粘贴...";
            var pasteResult = await _remoteClipboardCoordinator.PasteAsync(
                _session,
                currentShare,
                _directoryNavigationCoordinator.CurrentPath,
                FileList.ContainsName
            );
            if (pasteResult is null)
            {
                return;
            }

            Status.Message = pasteResult.OperationResult.Summary;
            if (pasteResult.ClearClipboard)
            {
                _remoteClipboardCoordinator.Clear();
            }

            if (pasteResult.OperationResult.Succeeded)
            {
                await RefreshCurrentDirectoryAsync();
            }
        }
        catch (Exception ex)
        {
            Status.Message = UserFacingError(ex, "粘贴失败");
        }
        finally
        {
            RefreshFileCommands();
        }
    }

    private void RefreshFileCommands()
    {
        if (FileList.CopyLinkCommand is AsyncRelayCommand copyLinkCommand)
        {
            copyLinkCommand.RaiseCanExecuteChanged();
        }

        if (FileList.CutCommand is RelayCommand cutCommand)
        {
            cutCommand.RaiseCanExecuteChanged();
        }

        if (FileList.CopyFileCommand is RelayCommand copyFileCommand)
        {
            copyFileCommand.RaiseCanExecuteChanged();
        }

        if (FileList.PasteCommand is AsyncRelayCommand pasteCommand)
        {
            pasteCommand.RaiseCanExecuteChanged();
        }

        if (FileList.RefreshCommand is AsyncRelayCommand refreshCommand)
        {
            refreshCommand.RaiseCanExecuteChanged();
        }

        if (FileList.GoUpCommand is AsyncRelayCommand goUpCommand)
        {
            goUpCommand.RaiseCanExecuteChanged();
        }

        if (FileList.CreateFolderCommand is AsyncRelayCommand createFolderCommand)
        {
            createFolderCommand.RaiseCanExecuteChanged();
        }

        if (FileList.DeleteCommand is AsyncRelayCommand deleteCommand)
        {
            deleteCommand.RaiseCanExecuteChanged();
        }

        if (FileList.RenameCommand is AsyncRelayCommand renameCommand)
        {
            renameCommand.RaiseCanExecuteChanged();
        }

        if (Preview.CopyLinkCommand is AsyncRelayCommand previewCommand)
        {
            previewCommand.RaiseCanExecuteChanged();
        }

        if (Navigation.AddFavoriteCommand is AsyncRelayCommand addFavoriteCommand)
        {
            addFavoriteCommand.RaiseCanExecuteChanged();
        }
    }

    private bool CanRefreshCurrentView() => _session is not null
        && (_directoryNavigationCoordinator.HasCurrentDirectory || FileList.IsShareRootView);

    private bool CanUseCurrentDirectory() => _session is not null
        && _directoryNavigationCoordinator.HasCurrentDirectory;

    private bool CanGoUpDirectory() => _session is not null
        && _directoryNavigationCoordinator.HasCurrentDirectory
        && !FileList.IsShareRootView;

    private bool CanPasteRemoteClipboard() => CanUseCurrentDirectory() && _remoteClipboardCoordinator.CanPaste;

    private async Task OpenSelectedItemAsync()
    {
        var selected = FileList.SelectedItem?.Item;
        if (selected is null || !selected.IsDirectory)
        {
            return;
        }

        if (selected.IsShareRoot)
        {
            await LoadDirectoryAsync(selected.Share, "/", null, expandNavigationNode: false);
            return;
        }

        await LoadDirectoryAsync(selected.Share, selected.Path, null, expandNavigationNode: false);
    }

    private async Task GoUpDirectoryAsync()
    {
        if (_session is null || _directoryNavigationCoordinator.CurrentShare is not { } currentShare)
        {
            return;
        }

        var currentPath = DirectoryNavigationCoordinator.NormalizeDirectoryPath(
            _directoryNavigationCoordinator.CurrentPath
        );
        if (currentPath == "/")
        {
            _directoryNavigationCoordinator.ShowShareRoot(
                _session,
                $"已返回全部共享，共 {_session.Shares.Count} 个共享。",
                RefreshFileCommands
            );
            return;
        }

        await LoadDirectoryAsync(currentShare, ParentPath(currentPath), null, expandNavigationNode: false);
    }

    private async Task<bool> LoadDirectoryAsync(
        string share,
        string path,
        NavigationNodeViewModel? navigationNode,
        bool? expandNavigationNode
    )
    {
        return await _directoryNavigationCoordinator.LoadAsync(
            _session,
            share,
            path,
            navigationNode,
            expandNavigationNode,
            RefreshFileCommands
        );
    }

    private static string UserFacingError(Exception ex, string fallback)
    {
        return ex.Message.Contains("auth", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("logon", StringComparison.OrdinalIgnoreCase)
                ? "账号或密码错误"
                : fallback;
    }

    private static bool FavoriteMatchesSession(FavoriteLinkItem favorite, ServerSession session)
    {
        return NormalizeServerHost(favorite.ServerHost)
            .Equals(NormalizeServerHost(session.Host), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeServerHost(string host)
    {
        var normalized = host.Trim();
        if (normalized.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["smb://".Length..];
        }

        normalized = normalized.Trim().TrimEnd('/').TrimEnd('.');
        return normalized.EndsWith(":445", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^":445".Length]
            : normalized;
    }

    private static string ParentPath(string path)
    {
        var normalized = DirectoryNavigationCoordinator.NormalizeDirectoryPath(path);
        if (normalized == "/")
        {
            return "/";
        }

        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash <= 0 ? "/" : normalized[..lastSlash];
    }
}
