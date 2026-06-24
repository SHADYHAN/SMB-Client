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
    private readonly IRemoteCopyMoveService _remoteCopyMoveService;
    private readonly IQuickLinkService _quickLinkService;
    private readonly IServerProfileService _serverProfileService;
    private readonly IClipboardService _clipboardService;
    private readonly IUserDialogService _userDialogService;
    private readonly IServerSettingsDialogService _serverSettingsDialogService;
    private ServerSession? _session;
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
        _sessionService = sessionService;
        _fileOperationService = fileOperationService;
        _remoteCopyMoveService = remoteCopyMoveService;
        _quickLinkService = quickLinkService;
        _serverProfileService = serverProfileService;
        _clipboardService = clipboardService;
        _userDialogService = userDialogService;
        _serverSettingsDialogService = serverSettingsDialogService;
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

        Login.LoginCommand = new AsyncRelayCommand(LoginAsync, CanLogin);
        Login.ServerSettingsCommand = new AsyncRelayCommand(OpenServerSettingsAsync, () => !Login.IsBusy);
        FileList.OpenItemCommand = new AsyncRelayCommand(OpenSelectedItemAsync, () => FileList.SelectedItem is not null);
        FileList.CopyLinkCommand = new AsyncRelayCommand(CopySelectedFileLinkAsync, () => FileList.SelectedItem is not null && _session is not null);
        FileList.CutCommand = new RelayCommand(CutSelectedItem, () => FileList.SelectedItem is not null && _session is not null);
        FileList.CopyFileCommand = new RelayCommand(CopySelectedItem, () => FileList.SelectedItem is not null && _session is not null);
        FileList.PasteCommand = new AsyncRelayCommand(PasteRemoteClipboardAsync, CanPasteRemoteClipboard);
        FileList.RefreshCommand = new AsyncRelayCommand(RefreshCurrentDirectoryAsync, CanUseCurrentDirectory);
        FileList.CreateFolderCommand = new AsyncRelayCommand(CreateFolderAsync, CanUseCurrentDirectory);
        FileList.DeleteCommand = new AsyncRelayCommand(DeleteSelectedItemAsync, () => FileList.SelectedItem is not null && _session is not null);
        FileList.RenameCommand = new AsyncRelayCommand(RenameSelectedItemAsync, () => FileList.SelectedItem is not null && _session is not null);
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

            await TryAutoLoginAsync();
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

    public async Task StartFileDragAsync(object dragSource, FileItemViewModel? item)
    {
        await _fileDragDropCoordinator.StartFileDragAsync(_session, dragSource, item);
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

    private async Task LoginAsync()
    {
        if (!CanLogin())
        {
            return;
        }

        Login.IsBusy = true;
        Login.Message = "正在连接...";
        Status.Message = "正在连接服务器...";

        try
        {
            var storedProfile = StoredCredentialProfileForLogin();
            var useStoredCredential = storedProfile is not null && string.IsNullOrEmpty(Login.Password);
            var result = useStoredCredential && storedProfile is not null
                ? await _sessionService.ConnectStoredCredentialAsync(storedProfile)
                : await _sessionService.ConnectAsync(Login.ServerHost, Login.Username, Login.Password);

            if (!result.Succeeded || result.Session is null)
            {
                Login.Message = result.Summary;
                Status.Message = result.Summary;
                return;
            }

            if (useStoredCredential && storedProfile is not null)
            {
                await UpdateStoredCredentialOptionsAsync(storedProfile);
            }
            else
            {
                await SaveLoginProfileAsync();
            }

            await CompleteLoginAsync(result.Session);
        }
        catch (Exception ex)
        {
            Login.Message = UserFacingError(ex, "登录失败");
            Status.Message = Login.Message;
        }
        finally
        {
            Login.IsBusy = false;
        }
    }

    private async Task TryAutoLoginAsync()
    {
        var profile = StoredCredentialProfileForLogin();
        if (IsLoggedIn || profile is null || !profile.AutoLogin)
        {
            return;
        }

        Login.IsBusy = true;
        Login.Message = "正在自动登录...";
        Status.Message = $"正在自动连接 {profile.DisplayName}...";

        try
        {
            var result = await _sessionService.ConnectStoredCredentialAsync(profile);
            if (!result.Succeeded || result.Session is null)
            {
                Login.Message = "自动登录失败，请手动登录。";
                Status.Message = result.Summary;
                return;
            }

            await CompleteLoginAsync(result.Session);
        }
        catch (Exception ex)
        {
            Login.Message = "自动登录失败，请手动登录。";
            Status.Message = UserFacingError(ex, "自动登录失败");
        }
        finally
        {
            Login.IsBusy = false;
        }
    }

    private async Task CompleteLoginAsync(ServerSession session)
    {
        _session = session;
        _remoteClipboardCoordinator.Clear();
        _directoryNavigationCoordinator.Clear();
        Navigation.LoadShares(_session.Shares);
        IsLoggedIn = true;
        Login.Password = "";
        FileList.Clear("");
        Preview.ShowSelection(null);
        RefreshFileCommands();
        Status.Message = $"已连接 {_session.Host}。";
        await ConsumePendingLinkIfPossibleAsync();
    }

    private async Task OpenServerSettingsAsync()
    {
        var activeProfile = Login.SelectedProfile;
        var result = await _serverSettingsDialogService.ShowAsync(Login.ServerProfiles.ToArray(), activeProfile);
        if (result is null)
        {
            return;
        }

        Login.ReplaceServerProfiles(result.Profiles, result.ActiveProfile);
        Status.Message = "服务器设置已更新。";
    }

    private async Task SaveLoginProfileAsync()
    {
        var saveResult = await _serverProfileService.SaveLoginAsync(
            MatchingSelectedProfile(),
            Login.ServerHost,
            Login.Username,
            Login.Password,
            Login.RememberPassword,
            Login.AutoLogin
        );

        if (saveResult.Succeeded && saveResult.Profile is not null)
        {
            Login.UpsertProfile(saveResult.Profile);
            return;
        }

        Status.Message = saveResult.Summary;
    }

    private async Task UpdateStoredCredentialOptionsAsync(ServerProfile profile)
    {
        var saveResult = await _serverProfileService.UpdateCredentialOptionsAsync(
            profile,
            Login.RememberPassword,
            Login.AutoLogin
        );

        if (saveResult.Succeeded && saveResult.Profile is not null)
        {
            Login.UpsertProfile(saveResult.Profile);
            return;
        }

        Status.Message = saveResult.Summary;
    }

    private ServerProfile? MatchingSelectedProfile()
    {
        var selected = Login.SelectedProfile;
        if (selected is null)
        {
            return null;
        }

        return selected.Host.Equals(Login.ServerHost.Trim(), StringComparison.OrdinalIgnoreCase)
            ? selected
            : null;
    }

    private ServerProfile? StoredCredentialProfileForLogin()
    {
        var profile = MatchingSelectedProfile();
        if (profile?.HasStoredCredential != true)
        {
            return null;
        }

        var profileUsername = profile.Username?.Trim() ?? string.Empty;
        return profileUsername.Equals(Login.Username.Trim(), StringComparison.OrdinalIgnoreCase)
            ? profile
            : null;
    }

    private bool CanLogin()
    {
        return !Login.IsBusy
            && !string.IsNullOrWhiteSpace(Login.ServerHost)
            && !string.IsNullOrWhiteSpace(Login.Username)
            && (!string.IsNullOrEmpty(Login.Password) || StoredCredentialProfileForLogin() is not null);
    }

    private async Task CopySelectedFileLinkAsync()
    {
        await CopyLinkAsync(FileList.SelectedItem?.Item);
    }

    private async Task CopyPreviewLinkAsync()
    {
        await CopyLinkAsync(Preview.SelectedItem);
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
            Status.Message = "链接已复制。";
        }
        catch (Exception ex)
        {
            Status.Message = UserFacingError(ex, "链接复制失败");
        }
    }

    private async Task RefreshCurrentDirectoryAsync()
    {
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

    private void CutSelectedItem()
    {
        if (FileList.SelectedItem?.Item is not { } item)
        {
            return;
        }

        Status.Message = _remoteClipboardCoordinator.Cut(item);
        RefreshFileCommands();
    }

    private void CopySelectedItem()
    {
        if (FileList.SelectedItem?.Item is not { } item)
        {
            return;
        }

        Status.Message = _remoteClipboardCoordinator.Copy(item);
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
    }

    private bool CanUseCurrentDirectory() => _session is not null && _directoryNavigationCoordinator.HasCurrentDirectory;

    private bool CanPasteRemoteClipboard() => CanUseCurrentDirectory() && _remoteClipboardCoordinator.CanPaste;

    private async Task OpenSelectedItemAsync()
    {
        var selected = FileList.SelectedItem?.Item;
        if (selected is null || !selected.IsDirectory)
        {
            return;
        }

        await LoadDirectoryAsync(selected.Share, selected.Path, null, expandNavigationNode: false);
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
}
