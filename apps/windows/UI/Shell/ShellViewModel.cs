using System.Windows;
using System.Windows.Input;
using Rynat.WindowsClient.Infrastructure;
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
    private readonly ISmbSessionService _sessionService;
    private readonly IFileOperationService _fileOperationService;
    private readonly IFileTransferService _fileTransferService;
    private readonly IQuickLinkService _quickLinkService;
    private readonly IClipboardService _clipboardService;
    private readonly IUserDialogService _userDialogService;
    private ServerSession? _session;
    private readonly LoginCoordinator _loginCoordinator;
    private readonly DirectoryNavigationCoordinator _directoryNavigationCoordinator;
    private readonly FileDragDropCoordinator _fileDragDropCoordinator;
    private readonly LinkActivationCoordinator _linkActivationCoordinator;
    private readonly PreviewCoordinator _previewCoordinator;
    private bool _isLoggedIn;
    private bool _isHandlingSessionDisconnect;

    public ShellViewModel(
        IBootstrapService bootstrapService,
        ISmbSessionService sessionService,
        IDirectoryService directoryService,
        IFileOperationService fileOperationService,
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
        _sessionService = sessionService;
        _fileOperationService = fileOperationService;
        _fileTransferService = fileTransferService;
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
            UserFacingError,
            HandleSessionIssueAsync
        );
        _fileDragDropCoordinator = new FileDragDropCoordinator(
            fileTransferService,
            _fileOperationService,
            shellDragDropService,
            _userDialogService,
            FileList,
            Status,
            UserFacingError,
            HandleSessionIssueAsync,
            HandleOperationResultAsync
        );
        _linkActivationCoordinator = new LinkActivationCoordinator(linkActivationService);
        _previewCoordinator = new PreviewCoordinator(previewService, FileList, Preview, HandleSessionIssueAsync);

        Login.LoginCommand = new AsyncRelayCommand(_loginCoordinator.LoginAsync, _loginCoordinator.CanLogin);
        Login.ServerSettingsCommand = new AsyncRelayCommand(_loginCoordinator.OpenServerSettingsAsync, () => !Login.IsBusy);
        FileList.OpenItemCommand = new AsyncRelayCommand(OpenSelectedItemAsync, () => FileList.HasSingleSelection);
        FileList.CopyLinkCommand = new AsyncRelayCommand(CopySelectedFileLinkAsync, () => FileList.HasSingleSelection && _session is not null);
        FileList.DownloadCommand = new AsyncRelayCommand(DownloadSelectedFilesAsync, () => FileList.HasWritableSelection && _session is not null);
        FileList.RefreshCommand = new AsyncRelayCommand(RefreshCurrentDirectoryAsync, CanRefreshCurrentView);
        FileList.GoUpCommand = new AsyncRelayCommand(GoUpDirectoryAsync, CanGoUpDirectory);
        FileList.GoShareRootCommand = new RelayCommand(GoShareRoot, CanGoShareRoot);
        FileList.CreateFolderCommand = new AsyncRelayCommand(CreateFolderAsync, CanUseCurrentDirectory);
        FileList.DeleteCommand = new AsyncRelayCommand(DeleteSelectedItemAsync, () => FileList.HasSingleWritableSelection && _session is not null);
        FileList.RenameCommand = new AsyncRelayCommand(RenameSelectedItemAsync, () => FileList.HasSingleWritableSelection && _session is not null);
        Navigation.ShowSharesCommand = new RelayCommand(Navigation.ShowShares);
        Navigation.ShowFavoritesCommand = new RelayCommand(Navigation.ShowFavorites);
        Navigation.AddFavoriteCommand = new AsyncRelayCommand(AddSelectedFavoriteAsync, () => FileList.HasSingleSelection && _session is not null);
        Navigation.RemoveFavoriteCommand = new AsyncRelayCommand(RemoveFavoriteAsync, parameter => parameter is FavoriteLinkViewModel);
        Preview.ToggleCommand = new RelayCommand(() => Preview.IsVisible = !Preview.IsVisible);
        Preview.CopyLinkCommand = new AsyncRelayCommand(CopyPreviewLinkAsync, () => Preview.SelectedItem is not null && _session is not null);
        LogoutCommand = new AsyncRelayCommand(LogoutAsync, () => _session is not null);
    }

    public LoginViewModel Login { get; } = new();

    public NavigationTreeViewModel Navigation { get; } = new();

    public FileListViewModel FileList { get; } = new();

    public PreviewPaneViewModel Preview { get; } = new();

    public StatusBarViewModel Status { get; } = new();

    public ICommand LogoutCommand { get; }

    public string HeaderUserLabel => string.IsNullOrWhiteSpace(Login.Username)
        ? "用户"
        : Login.Username;

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
        if (_session is null || !await EnsureActiveSessionAsync())
        {
            return;
        }

        await LoadDirectoryAsync(node.Share, node.Path, node, expandNavigationNode: null);
    }

    public void ReportUiError(Exception exception, string fallback)
    {
        if (BridgeExceptionClassifier.IsReconnectable(exception))
        {
            _ = HandleSessionDisconnectedAsync();
            return;
        }

        Status.Message = UserFacingError(exception, fallback);
    }

    public async Task ActivateExternalArgumentsAsync(IReadOnlyList<string> rawArguments)
    {
        await ActivateStartupLinkAsync(rawArguments);
    }

    public async Task ShutdownAsync()
    {
        await DisconnectCurrentSessionAsync(updateUi: false);
    }

    public async Task ToggleNavigationNodeAsync(NavigationNodeViewModel node)
    {
        if (_session is null || !await EnsureActiveSessionAsync())
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
        if (_session is not null && item?.Item is { IsDirectory: false } && !await EnsureActiveSessionAsync())
        {
            return;
        }

        await _previewCoordinator.SelectFileAsync(_session, item, RefreshFileCommands);
    }

    public async Task StartFileDragAsync(
        object dragSource,
        FileItemViewModel? item,
        IReadOnlyList<RemoteFileItem>? preservedSelection = null
    )
    {
        if (!await EnsureActiveSessionAsync())
        {
            return;
        }

        await _fileDragDropCoordinator.StartFileDragAsync(
            _session,
            dragSource,
            item,
            preservedSelection
        );
    }

    public async Task UploadDroppedFilesAsync(IReadOnlyList<string> localPaths)
    {
        if (!await EnsureActiveSessionAsync())
        {
            return;
        }

        await _fileDragDropCoordinator.UploadDroppedFilesAsync(
            _session,
            _directoryNavigationCoordinator.CurrentShare,
            _directoryNavigationCoordinator.CurrentPath,
            localPaths,
            RefreshCurrentDirectoryAsync
        );
    }

    public async Task OpenFavoriteAsync(FavoriteLinkViewModel favorite)
    {
        if (_session is null || !await EnsureActiveSessionAsync())
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
        Navigation.LoadShares(_session.Shares);
        IsLoggedIn = true;
        Login.Password = "";
        _directoryNavigationCoordinator.ShowShareRoot(
            _session,
            $"已显示 {_session.Shares.Count} 个共享。",
            RefreshFileCommands
        );
        await LoadFavoritesAsync();
        OnPropertyChanged(nameof(HeaderUserLabel));
        RefreshShellCommands();
        Status.Message = $"已连接 {_session.Host}。";
        await ConsumePendingLinkIfPossibleAsync();
    }

    private async Task LogoutAsync()
    {
        Status.Message = "正在退出登录...";
        var disconnected = await DisconnectCurrentSessionAsync(updateUi: true);
        if (disconnected)
        {
            Status.Message = "已退出登录。";
        }
    }

    private async Task<bool> DisconnectCurrentSessionAsync(bool updateUi)
    {
        var session = _session;
        _previewCoordinator.Cancel();
        _directoryNavigationCoordinator.Clear();

        if (session is not null)
        {
            _session = null;
            try
            {
                await _sessionService.DisconnectAsync(session);
            }
            catch (Exception ex)
            {
                if (updateUi)
                {
                    Status.Message = UserFacingError(ex, "退出登录时断开连接失败");
                }

                ResetSessionUi();
                return false;
            }
        }

        ResetSessionUi();
        return true;
    }

    private void ResetSessionUi()
    {
        Navigation.Roots.Clear();
        Navigation.SelectedNode = null;
        Navigation.LoadFavorites(Array.Empty<FavoriteLinkItem>());
        Navigation.ShowShares();
        FileList.Clear("未连接");
        Preview.ShowSelection(null);
        Login.Password = "";
        IsLoggedIn = false;
        OnPropertyChanged(nameof(HeaderUserLabel));
        RefreshFileCommands();
        RefreshShellCommands();
    }

    private async Task<bool> HandleSessionIssueAsync(Exception exception)
    {
        if (!BridgeExceptionClassifier.IsReconnectable(exception))
        {
            return false;
        }

        await HandleSessionDisconnectedAsync();
        return true;
    }

    private async Task<bool> HandleOperationResultAsync(FileOperationResult result)
    {
        if (result.Succeeded || !BridgeExceptionClassifier.IsReconnectableCode(result.ErrorCode))
        {
            return false;
        }

        await HandleSessionDisconnectedAsync();
        return true;
    }

    private async Task HandleSessionDisconnectedAsync()
    {
        if (_isHandlingSessionDisconnect)
        {
            return;
        }

        _isHandlingSessionDisconnect = true;
        try
        {
            await DisconnectCurrentSessionAsync(updateUi: false);
            Login.Message = "连接已断开，请重新登录。";
            Status.Message = "连接已断开，请重新登录。";
        }
        finally
        {
            _isHandlingSessionDisconnect = false;
        }
    }

    private async Task<bool> EnsureActiveSessionAsync()
    {
        var session = _session;
        if (session is null)
        {
            return false;
        }

        var connected = await _sessionService.IsConnectedAsync(session);
        if (connected)
        {
            return true;
        }

        await HandleSessionDisconnectedAsync();
        return false;
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
        if (_session is null || item is null || !await EnsureActiveSessionAsync())
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
        if (!await EnsureActiveSessionAsync())
        {
            return;
        }

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
        if (_session is null || currentShare is null || !await EnsureActiveSessionAsync())
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
        if (await HandleOperationResultAsync(result))
        {
            return;
        }

        Status.Message = result.Summary;
        if (result.Succeeded)
        {
            await RefreshCurrentDirectoryAsync();
        }
    }

    private async Task DeleteSelectedItemAsync()
    {
        if (_session is null || FileList.SelectedItem?.Item is not { } item || !await EnsureActiveSessionAsync())
        {
            return;
        }

        if (!_userDialogService.Confirm("删除", $"确定删除 {item.Name} 吗？"))
        {
            return;
        }

        var result = await _fileOperationService.DeleteAsync(_session, item);
        if (await HandleOperationResultAsync(result))
        {
            return;
        }

        Status.Message = result.Summary;
        if (result.Succeeded)
        {
            await RefreshCurrentDirectoryAsync();
        }
    }

    private async Task RenameSelectedItemAsync()
    {
        if (_session is null || FileList.SelectedItem?.Item is not { } item || !await EnsureActiveSessionAsync())
        {
            return;
        }

        var name = _userDialogService.PromptText("重命名", "名称", item.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var result = await _fileOperationService.RenameAsync(_session, item, name);
        if (await HandleOperationResultAsync(result))
        {
            return;
        }

        Status.Message = result.Summary;
        if (result.Succeeded)
        {
            await RefreshCurrentDirectoryAsync();
        }
    }

    private async Task DownloadSelectedFilesAsync()
    {
        if (_session is null || !await EnsureActiveSessionAsync())
        {
            return;
        }

        var items = FileList.SelectedRemoteItems
            .Where(item => !item.IsShareRoot)
            .ToArray();
        if (items.Length == 0)
        {
            return;
        }

        if (items.Any(item => item.IsDirectory))
        {
            Status.Message = "暂不支持下载文件夹。";
            return;
        }

        var localPaths = ChooseDownloadPaths(items);
        if (localPaths is null)
        {
            Status.Message = "已取消下载。";
            return;
        }

        var conflicts = localPaths
            .Where(System.IO.File.Exists)
            .Select(System.IO.Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (conflicts.Length > 0 && !_userDialogService.ConfirmOverwrite(conflicts))
        {
            Status.Message = "已取消下载。";
            return;
        }

        try
        {
            Status.BeginTask("正在下载...", items.Length == 1 ? items[0].Name : $"0/{items.Length}");
            var progress = new Progress<FileBatchProgress>(item =>
                Status.ReportTaskProgress(item.Completed, item.Total, item.CurrentName)
            );
            var result = await _fileTransferService.DownloadFilesAsync(
                _session,
                items,
                localPaths,
                replaceExisting: conflicts.Length > 0,
                progress
            );
            if (await HandleTransferResultAsync(result))
            {
                return;
            }

            Status.EndTask(result.Summary);
        }
        catch (Exception ex)
        {
            if (await HandleSessionIssueAsync(ex))
            {
                return;
            }

            Status.EndTask(UserFacingError(ex, "下载失败"));
        }
        finally
        {
            if (Status.IsBusy)
            {
                Status.ClearTask();
            }
        }
    }

    private async Task<bool> HandleTransferResultAsync(DragFilePayloadResult result)
    {
        if (result.Succeeded || !BridgeExceptionClassifier.IsReconnectableCode(result.ErrorCode))
        {
            return false;
        }

        await HandleSessionDisconnectedAsync();
        return true;
    }

    private IReadOnlyList<string>? ChooseDownloadPaths(IReadOnlyList<RemoteFileItem> items)
    {
        if (items.Count == 1)
        {
            var path = _userDialogService.PickSaveFilePath("下载到", items[0].Name);
            return string.IsNullOrWhiteSpace(path)
                ? null
                : new[] { path };
        }

        var folderPath = _userDialogService.PickFolderPath("选择下载位置");
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return null;
        }

        return items
            .Select(item => System.IO.Path.Combine(folderPath, SafeLocalFileName(item.Name)))
            .ToArray();
    }

    private void RefreshFileCommands()
    {
        if (FileList.CopyLinkCommand is AsyncRelayCommand copyLinkCommand)
        {
            copyLinkCommand.RaiseCanExecuteChanged();
        }

        if (FileList.DownloadCommand is AsyncRelayCommand downloadCommand)
        {
            downloadCommand.RaiseCanExecuteChanged();
        }

        if (FileList.RefreshCommand is AsyncRelayCommand refreshCommand)
        {
            refreshCommand.RaiseCanExecuteChanged();
        }

        if (FileList.GoUpCommand is AsyncRelayCommand goUpCommand)
        {
            goUpCommand.RaiseCanExecuteChanged();
        }

        if (FileList.GoShareRootCommand is RelayCommand goShareRootCommand)
        {
            goShareRootCommand.RaiseCanExecuteChanged();
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

    private void RefreshShellCommands()
    {
        if (LogoutCommand is AsyncRelayCommand logoutCommand)
        {
            logoutCommand.RaiseCanExecuteChanged();
        }
    }

    private bool CanRefreshCurrentView() => _session is not null
        && (_directoryNavigationCoordinator.HasCurrentDirectory || FileList.IsShareRootView);

    private bool CanUseCurrentDirectory() => _session is not null
        && _directoryNavigationCoordinator.HasCurrentDirectory;

    private bool CanGoUpDirectory() => _session is not null
        && _directoryNavigationCoordinator.HasCurrentDirectory
        && !FileList.IsShareRootView;

    private bool CanGoShareRoot() => _session is not null && !FileList.IsShareRootView;

    private async Task OpenSelectedItemAsync()
    {
        if (!await EnsureActiveSessionAsync())
        {
            return;
        }

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
        if (_session is null || _directoryNavigationCoordinator.CurrentShare is not { } currentShare || !await EnsureActiveSessionAsync())
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

    private void GoShareRoot()
    {
        if (_session is null)
        {
            return;
        }

        _directoryNavigationCoordinator.ShowShareRoot(
            _session,
            $"已返回全部共享，共 {_session.Shares.Count} 个共享。",
            RefreshFileCommands
        );
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

    private static string SafeLocalFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var safe = string.Concat(name.Select(ch => invalid.Contains(ch) ? '_' : ch));
        return string.IsNullOrWhiteSpace(safe) ? "download" : safe;
    }
}
