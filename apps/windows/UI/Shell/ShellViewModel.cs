using System.Threading;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Platform.Clipboard;
using Rynat.WindowsClient.Platform.Dialogs;
using Rynat.WindowsClient.Platform.Shell;
using Rynat.WindowsClient.Services.Bootstrap;
using Rynat.WindowsClient.Services.Directory;
using Rynat.WindowsClient.Services.FileOperations;
using Rynat.WindowsClient.Services.FileTransfers;
using Rynat.WindowsClient.Services.Links;
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
    private readonly IDirectoryService _directoryService;
    private readonly IFileOperationService _fileOperationService;
    private readonly IFileTransferService _fileTransferService;
    private readonly IQuickLinkService _quickLinkService;
    private readonly IPreviewService _previewService;
    private readonly IServerProfileService _serverProfileService;
    private readonly IClipboardService _clipboardService;
    private readonly IUserDialogService _userDialogService;
    private readonly IWindowsShellDragDropService _shellDragDropService;
    private ServerSession? _session;
    private string? _currentShare;
    private string _currentPath = "/";
    private string? _loadingDirectoryKey;
    private int _previewLoadVersion;
    private bool _isLoggedIn;

    public ShellViewModel(
        IBootstrapService bootstrapService,
        ISmbSessionService sessionService,
        IDirectoryService directoryService,
        IFileOperationService fileOperationService,
        IFileTransferService fileTransferService,
        IQuickLinkService quickLinkService,
        IPreviewService previewService,
        IServerProfileService serverProfileService,
        IClipboardService clipboardService,
        IUserDialogService userDialogService,
        IWindowsShellDragDropService shellDragDropService
    )
    {
        _bootstrapService = bootstrapService;
        _sessionService = sessionService;
        _directoryService = directoryService;
        _fileOperationService = fileOperationService;
        _fileTransferService = fileTransferService;
        _quickLinkService = quickLinkService;
        _previewService = previewService;
        _serverProfileService = serverProfileService;
        _clipboardService = clipboardService;
        _userDialogService = userDialogService;
        _shellDragDropService = shellDragDropService;

        Login.LoginCommand = new AsyncRelayCommand(LoginAsync, CanLogin);
        FileList.OpenItemCommand = new AsyncRelayCommand(OpenSelectedItemAsync, () => FileList.SelectedItem is not null);
        FileList.CopyLinkCommand = new AsyncRelayCommand(CopySelectedFileLinkAsync, () => FileList.SelectedItem is not null && _session is not null);
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

    public async Task InitializeAsync()
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

    public async Task ToggleNavigationNodeAsync(NavigationNodeViewModel node)
    {
        if (_session is null)
        {
            return;
        }

        var shouldExpand = !node.IsExpanded;
        await LoadDirectoryAsync(node.Share, node.Path, node, expandNavigationNode: shouldExpand);
        node.IsExpanded = shouldExpand;
    }

    public async Task SelectFileAsync(FileItemViewModel? item)
    {
        FileList.SelectedItem = item;
        Preview.ShowSelection(item?.Item);
        RefreshFileCommands();

        var previewVersion = Interlocked.Increment(ref _previewLoadVersion);
        if (_session is not null && item?.Item is { IsDirectory: false } selected)
        {
            try
            {
                Preview.ShowPreviewLoading();
                var info = await _previewService.PlanAsync(_session, selected);
                if (previewVersion == _previewLoadVersion && ReferenceEquals(FileList.SelectedItem, item))
                {
                    Preview.ShowPreviewInfo(info);
                }
            }
            catch
            {
                if (previewVersion == _previewLoadVersion && ReferenceEquals(FileList.SelectedItem, item))
                {
                    Preview.ShowPreviewUnavailable();
                }
            }
        }
    }

    public async Task StartFileDragAsync(object dragSource, FileItemViewModel? item)
    {
        if (_session is null || item is null)
        {
            return;
        }

        var selectedItems = new[] { item.Item };

        if (!_shellDragDropService.CanStartDrag(selectedItems))
        {
            Status.Message = item.Item.IsDirectory ? "暂不支持拖出文件夹。" : "无法拖出。";
            return;
        }

        try
        {
            Status.Message = "拖到本地位置后开始复制。";
            var result = await _fileTransferService.CreateDragDownloadPayloadAsync(_session, selectedItems);
            if (!result.Succeeded)
            {
                Status.Message = result.Summary;
                return;
            }

            var completed = _shellDragDropService.StartDrag(dragSource, result.Files);
            Status.Message = completed ? "拖出完成。" : "已取消拖出。";
        }
        catch (Exception ex)
        {
            Status.Message = UserFacingError(ex, "拖出失败");
        }
    }

    public async Task UploadDroppedFilesAsync(IReadOnlyList<string> localPaths)
    {
        if (_session is null || _currentShare is null)
        {
            return;
        }

        var existingNames = new HashSet<string>(
            FileList.Items.Select(item => item.Name),
            StringComparer.CurrentCultureIgnoreCase
        );
        var conflicts = localPaths
            .Select(System.IO.Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && existingNames.Contains(name))
            .Cast<string>()
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (conflicts.Length > 0 && !_userDialogService.ConfirmOverwrite(conflicts))
        {
            Status.Message = "已取消上传。";
            return;
        }

        try
        {
            Status.Message = "正在上传...";
            var result = await _fileOperationService.UploadFilesAsync(
                _session,
                _currentShare,
                _currentPath,
                localPaths,
                replaceExisting: conflicts.Length > 0
            );
            Status.Message = result.Summary;
            if (result.Succeeded)
            {
                await RefreshCurrentDirectoryAsync();
            }
        }
        catch (Exception ex)
        {
            Status.Message = UserFacingError(ex, "上传失败");
        }
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
        Navigation.LoadShares(_session.Shares);
        IsLoggedIn = true;
        RefreshFileCommands();
        Login.Password = "";
        Status.Message = $"已连接 {_session.Host}，协议 {_session.DialectLabel}。";

        var firstShare = Navigation.Roots.FirstOrDefault();
        if (firstShare is not null)
        {
            await LoadDirectoryAsync(firstShare.Share, firstShare.Path, firstShare, expandNavigationNode: true);
        }
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
        if (_currentShare is null)
        {
            return;
        }

        await LoadDirectoryAsync(_currentShare, _currentPath, CurrentNavigationNode(), expandNavigationNode: null);
    }

    private async Task CreateFolderAsync()
    {
        if (_session is null || _currentShare is null)
        {
            return;
        }

        var name = _userDialogService.PromptText("新建文件夹", "文件夹名称", "新建文件夹");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var result = await _fileOperationService.CreateDirectoryAsync(_session, _currentShare, _currentPath, name);
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

    private void RefreshFileCommands()
    {
        if (FileList.CopyLinkCommand is AsyncRelayCommand copyLinkCommand)
        {
            copyLinkCommand.RaiseCanExecuteChanged();
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

    private bool CanUseCurrentDirectory() => _session is not null && _currentShare is not null;

    private NavigationNodeViewModel? CurrentNavigationNode()
    {
        var selected = Navigation.SelectedNode;
        return selected is not null
            && _currentShare is not null
            && selected.Share.Equals(_currentShare, StringComparison.OrdinalIgnoreCase)
            && NormalizeDirectoryPath(selected.Path) == NormalizeDirectoryPath(_currentPath)
                ? selected
                : null;
    }

    private async Task OpenSelectedItemAsync()
    {
        var selected = FileList.SelectedItem?.Item;
        if (selected is null || !selected.IsDirectory)
        {
            return;
        }

        await LoadDirectoryAsync(selected.Share, selected.Path, null, expandNavigationNode: false);
    }

    private async Task LoadDirectoryAsync(
        string share,
        string path,
        NavigationNodeViewModel? navigationNode,
        bool? expandNavigationNode
    )
    {
        if (_session is null)
        {
            return;
        }

        var normalizedKey = $"{share}:{NormalizeDirectoryPath(path)}";
        if (_loadingDirectoryKey == normalizedKey)
        {
            if (navigationNode is not null && expandNavigationNode is bool requestedExpansion)
            {
                navigationNode.IsExpanded = requestedExpansion;
            }

            return;
        }

        _loadingDirectoryKey = normalizedKey;
        FileList.IsLoading = true;
        Status.Message = "正在加载目录...";

        try
        {
            var directory = await _directoryService.ListAsync(_session, share, path);
            _currentShare = directory.Share;
            _currentPath = directory.Path;
            FileList.ShowDirectory(directory);
            Preview.ShowSelection(null);
            RefreshFileCommands();

            if (navigationNode is not null)
            {
                Navigation.ReplaceChildren(
                    navigationNode,
                    directory.Items.Where(item => item.IsDirectory).ToArray()
                );
                if (expandNavigationNode is bool requestedExpansion)
                {
                    navigationNode.IsExpanded = requestedExpansion;
                }

                navigationNode.IsSelected = true;
                Navigation.SelectedNode = navigationNode;
            }

            Status.Message = $"{directory.Items.Count} 个项目";
        }
        catch (Exception ex)
        {
            Status.Message = UserFacingError(ex, "目录加载失败");
        }
        finally
        {
            if (_loadingDirectoryKey == normalizedKey)
            {
                _loadingDirectoryKey = null;
            }

            FileList.IsLoading = false;
        }
    }

    private static string NormalizeDirectoryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        var normalized = path.Replace('\\', '/').Trim();
        return normalized.StartsWith('/') ? normalized : "/" + normalized;
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
