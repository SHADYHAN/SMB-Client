using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Platform.Shell;
using Rynat.WindowsClient.Services.Bootstrap;
using Rynat.WindowsClient.Services.Directory;
using Rynat.WindowsClient.Services.Links;
using Rynat.WindowsClient.Services.Preview;
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
    private readonly IQuickLinkService _quickLinkService;
    private readonly IPreviewService _previewService;
    private readonly IWindowsShellDragDropService _shellDragDropService;
    private ServerSession? _session;
    private string? _loadingDirectoryKey;
    private bool _isLoggedIn;

    public ShellViewModel(
        IBootstrapService bootstrapService,
        ISmbSessionService sessionService,
        IDirectoryService directoryService,
        IQuickLinkService quickLinkService,
        IPreviewService previewService,
        IWindowsShellDragDropService shellDragDropService
    )
    {
        _bootstrapService = bootstrapService;
        _sessionService = sessionService;
        _directoryService = directoryService;
        _quickLinkService = quickLinkService;
        _previewService = previewService;
        _shellDragDropService = shellDragDropService;

        Login.LoginCommand = new AsyncRelayCommand(LoginAsync, CanLogin);
        FileList.OpenItemCommand = new AsyncRelayCommand(OpenSelectedItemAsync, () => FileList.SelectedItem is not null);
        Preview.ToggleCommand = new RelayCommand(() => Preview.IsVisible = !Preview.IsVisible);
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
            if (state.ActiveServer is not null)
            {
                Login.ServerHost = state.ActiveServer.Host;
                Login.Username = state.ActiveUsername ?? state.ActiveServer.Username ?? "";
            }

            Status.Message = state.ServerProfiles.Count == 0
                ? "未找到已保存服务器，已填入默认服务器地址。"
                : $"已加载 {state.ServerProfiles.Count} 个服务器配置。";
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

        await LoadDirectoryAsync(node.Share, node.Path, node);
    }

    public async Task SelectFileAsync(FileItemViewModel? item)
    {
        FileList.SelectedItem = item;
        Preview.ShowSelection(item?.Item);

        if (item?.Item is { IsDirectory: false } selected)
        {
            try
            {
                var info = await _previewService.PlanAsync(_session!, selected);
                Preview.ShowPreviewInfo(info);
            }
            catch
            {
                Preview.ContentType = "";
            }
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
            var result = await _sessionService.ConnectAsync(
                Login.ServerHost,
                Login.Username,
                Login.Password
            );

            if (!result.Succeeded || result.Session is null)
            {
                Login.Message = result.Summary;
                Status.Message = result.Summary;
                return;
            }

            _session = result.Session;
            Navigation.LoadShares(_session.Shares);
            IsLoggedIn = true;
            Login.Password = "";
            Status.Message = $"已连接 {_session.Host}，协议 {_session.DialectLabel}。";

            var firstShare = Navigation.Roots.FirstOrDefault();
            if (firstShare is not null)
            {
                await LoadDirectoryAsync(firstShare.Share, firstShare.Path, firstShare);
            }
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

    private bool CanLogin()
    {
        return !Login.IsBusy
            && !string.IsNullOrWhiteSpace(Login.ServerHost)
            && !string.IsNullOrWhiteSpace(Login.Username)
            && !string.IsNullOrEmpty(Login.Password);
    }

    private async Task OpenSelectedItemAsync()
    {
        var selected = FileList.SelectedItem?.Item;
        if (selected is null || !selected.IsDirectory)
        {
            return;
        }

        await LoadDirectoryAsync(selected.Share, selected.Path, null);
    }

    private async Task LoadDirectoryAsync(
        string share,
        string path,
        NavigationNodeViewModel? navigationNode
    )
    {
        if (_session is null)
        {
            return;
        }

        var normalizedKey = $"{share}:{NormalizeDirectoryPath(path)}";
        if (_loadingDirectoryKey == normalizedKey)
        {
            return;
        }

        _loadingDirectoryKey = normalizedKey;
        FileList.IsLoading = true;
        Status.Message = "正在加载目录...";

        try
        {
            var directory = await _directoryService.ListAsync(_session, share, path);
            FileList.ShowDirectory(directory);
            Preview.ShowSelection(null);

            if (navigationNode is not null)
            {
                Navigation.ReplaceChildren(
                    navigationNode,
                    directory.Items.Where(item => item.IsDirectory).ToArray()
                );
                navigationNode.IsExpanded = true;
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
