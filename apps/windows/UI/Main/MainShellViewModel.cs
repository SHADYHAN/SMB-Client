using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Rynat.Client;
using Rynat.WindowsClient.AppServices.Bootstrap;
using Rynat.WindowsClient.AppServices.Directory;
using Rynat.WindowsClient.AppServices.Files;
using Rynat.WindowsClient.AppServices.Links;
using Rynat.WindowsClient.AppServices.Preview;
using Rynat.WindowsClient.AppServices.Smb;
using Rynat.WindowsClient.AppServices.Tasks;
using Rynat.WindowsClient.Infrastructure;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Text;

namespace Rynat.WindowsClient.UI.Main;

public enum SidebarTab
{
    Shares,
    Favorites
}

public sealed class MainShellViewModel : ObservableObject, IDisposable
{
    private readonly AppBootstrapService _bootstrapService;
    private readonly ServerProfileManagementService _serverProfileManagementService;
    private readonly SmbSessionService _smbSessionService;
    private readonly DirectoryBrowserService _directoryBrowserService;
    private readonly FileOpenService _fileOpenService;
    private readonly FileDownloadService _fileDownloadService;
    private readonly FileDragDownloadPreparationService? _fileDragDownloadPreparationService;
    private readonly FileBatchOperationService? _fileBatchOperationService;
    private readonly FileWriteService _fileWriteService;
    private readonly WindowsFileTaskService? _fileTaskService;
    private readonly LinkActivationService _linkActivationService;
    private readonly LinkShareService _linkShareService;
    private readonly QuickLinkLibraryService? _quickLinkLibraryService;
    private readonly PreviewEntryService _previewEntryService;
    private readonly WindowsClientDiagnostics _diagnostics;
    private readonly DispatcherQueue? _dispatcherQueue;

    private AppBootstrapState? _bootstrapSnapshot;
    private WindowsServerSession? _session;
    private bool _isBusy;
    private int _busyOperationCount;
    private bool _canConnectSelectedProfile;
    private bool _canConnectWithCredentials;
    private bool _canNavigateUp;
    private string _bootstrapStatus = "尚未加载启动信息。";
    private string _statusText = string.Empty;
    private string _currentPath = "/";
    private string _connectionSummary = "尚未连接";
    private string _directorySummary = "请先加载启动信息。";
    private string _selectedProfileSummary = "尚未选择服务器。";
    private string _userMenuTitle = "用户";
    private string _manualUsername = string.Empty;
    private string _manualPassword = string.Empty;

    /// <summary>
    /// 切换服务器配置等场景需要清空登录页密码框时触发，由 code-behind 订阅并清空 PasswordBox。
    /// </summary>
    public event Action? PasswordInputClearRequested;
    private string _generatedLink = string.Empty;
    private string _manualConnectHint = string.Empty;
    private string _searchText = string.Empty;
    private string _activeTaskSummary = string.Empty;
    private string? _activeTaskId;
    private double _activeTaskProgress;
    private bool _hasActiveTask;
    private bool _isWorkspaceVisible;
    private bool _isAutoLoginConnecting;
    private bool _isPreviewPaneVisible = true;
    private bool _rememberPassword = true;
    private bool _autoLogin;
    private int _previewRequestVersion;
    private int _directoryRequestVersion;
    private LinkActivation? _pendingActivation;
    private FileClipboardState? _fileClipboard;
    private PreviewPaneState _previewPane;
    private readonly HashSet<string> _expandedSidebarPaths = new(StringComparer.OrdinalIgnoreCase);
    private ServerProfileListItem? _selectedProfile;
    private ShareListItem? _selectedShare;
    private SidebarTab _activeSidebarTab = SidebarTab.Shares;
    private SidebarItemViewModel? _selectedSidebarItem;
    private DirectoryItemViewModel? _selectedDirectoryItem;
    private IReadOnlyList<DirectoryItemViewModel> _currentDirectoryItems = [];
    private IReadOnlyList<QuickLink> _quickLinks = [];

    public MainShellViewModel(
        AppBootstrapService bootstrapService,
        ServerProfileManagementService serverProfileManagementService,
        SmbSessionService smbSessionService,
        DirectoryBrowserService directoryBrowserService,
        FileOpenService fileOpenService,
        FileDownloadService fileDownloadService,
        FileBatchOperationService? fileBatchOperationService,
        FileWriteService fileWriteService,
        WindowsFileTaskService? fileTaskService,
        LinkActivationService linkActivationService,
        LinkShareService linkShareService,
        PreviewEntryService previewEntryService,
        WindowsClientDiagnostics diagnostics,
        QuickLinkLibraryService? quickLinkLibraryService = null
    )
    {
        _bootstrapService = bootstrapService;
        _serverProfileManagementService = serverProfileManagementService;
        _smbSessionService = smbSessionService;
        _directoryBrowserService = directoryBrowserService;
        _fileOpenService = fileOpenService;
        _fileDownloadService = fileDownloadService;
        _fileBatchOperationService = fileBatchOperationService;
        _fileDragDownloadPreparationService = null;
        _fileWriteService = fileWriteService;
        _fileTaskService = fileTaskService;
        _linkActivationService = linkActivationService;
        _linkShareService = linkShareService;
        _quickLinkLibraryService = quickLinkLibraryService;
        _previewEntryService = previewEntryService;
        _diagnostics = diagnostics;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _previewPane = _previewEntryService.BuildArchitecturePlaceholder();
        if (_fileTaskService is not null)
        {
            _fileTaskService.TaskChanged += FileTaskService_TaskChanged;
        }
    }

    public MainShellViewModel(
        AppBootstrapService bootstrapService,
        ServerProfileManagementService serverProfileManagementService,
        SmbSessionService smbSessionService,
        DirectoryBrowserService directoryBrowserService,
        FileOpenService fileOpenService,
        FileDownloadService fileDownloadService,
        FileDragDownloadPreparationService fileDragDownloadPreparationService,
        FileBatchOperationService fileBatchOperationService,
        FileWriteService fileWriteService,
        WindowsFileTaskService fileTaskService,
        LinkActivationService linkActivationService,
        LinkShareService linkShareService,
        QuickLinkLibraryService quickLinkLibraryService,
        PreviewEntryService previewEntryService,
        WindowsClientDiagnostics diagnostics
    ) : this(
        bootstrapService,
        serverProfileManagementService,
        smbSessionService,
        directoryBrowserService,
        fileOpenService,
        fileDownloadService,
        fileBatchOperationService,
        fileWriteService,
        fileTaskService,
        linkActivationService,
        linkShareService,
        previewEntryService,
        diagnostics,
        quickLinkLibraryService
    )
    {
        _fileDragDownloadPreparationService = fileDragDownloadPreparationService;
    }

    public string WindowTitle => "RYNAT 共享网盘";

    public AppBootstrapState? BootstrapSnapshot => _bootstrapSnapshot;

    public bool IsWorkspaceVisible
    {
        get => _isWorkspaceVisible;
        private set
        {
            if (!SetProperty(ref _isWorkspaceVisible, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsLoginVisible));
        }
    }

    public bool IsLoginVisible => !IsWorkspaceVisible;

    public bool IsAutoLoginConnecting
    {
        get => _isAutoLoginConnecting;
        private set => SetProperty(ref _isAutoLoginConnecting, value);
    }

    public bool IsPreviewPaneVisible
    {
        get => _isPreviewPaneVisible;
        private set
        {
            if (SetProperty(ref _isPreviewPaneVisible, value))
            {
                OnPropertyChanged(nameof(PreviewPaneWidth));
                OnPropertyChanged(nameof(PreviewToggleGlyph));
                OnPropertyChanged(nameof(PreviewToggleLabel));
            }
        }
    }

    public GridLength PreviewPaneWidth => IsPreviewPaneVisible
        ? new GridLength(316)
        : new GridLength(0);

    public string PreviewToggleGlyph => "\uE977";

    public string PreviewToggleLabel => IsPreviewPaneVisible ? "关闭预览面板" : "打开预览面板";

    public void Dispose()
    {
        if (_fileTaskService is not null)
        {
            _fileTaskService.TaskChanged -= FileTaskService_TaskChanged;
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (value)
            {
                if (Interlocked.Increment(ref _busyOperationCount) == 1)
                {
                    if (SetProperty(ref _isBusy, true))
                    {
                        OnPropertyChanged(nameof(BusyIndicatorVisibility));
                    }
                }
                return;
            }

            var remaining = Interlocked.Decrement(ref _busyOperationCount);
            if (remaining <= 0)
            {
                Interlocked.Exchange(ref _busyOperationCount, 0);
                if (SetProperty(ref _isBusy, false))
                {
                    OnPropertyChanged(nameof(BusyIndicatorVisibility));
                }
            }
        }
    }

    public Visibility BusyIndicatorVisibility => IsBusy
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool CanConnectSelectedProfile
    {
        get => _canConnectSelectedProfile;
        private set
        {
            if (SetProperty(ref _canConnectSelectedProfile, value))
            {
                OnPropertyChanged(nameof(CanLogin));
            }
        }
    }

    public bool CanNavigateUp
    {
        get => _canNavigateUp;
        private set => SetProperty(ref _canNavigateUp, value);
    }

    public bool CanConnectWithCredentials
    {
        get => _canConnectWithCredentials;
        private set
        {
            if (SetProperty(ref _canConnectWithCredentials, value))
            {
                OnPropertyChanged(nameof(CanLogin));
            }
        }
    }

    public bool CanLogin => SelectedProfile is not null;

    public bool CanOpenSelectedItem => _session is not null && SelectedDirectoryItem is not null;

    public bool CanDownloadSelectedItem =>
        _session is not null &&
        SelectedDirectoryItem is not null;

    public bool CanCreateDirectory => _session is not null && CurrentPath != "/";

    public bool CanUploadFiles => _session is not null && CurrentPath != "/";

    public bool CanUploadToDirectory => _session is not null;

    public bool CanRenameSelectedItem =>
        _session is not null &&
        SelectedDirectoryItem is not null &&
        CurrentPath != "/";

    public bool CanDeleteSelectedItem =>
        _session is not null &&
        SelectedDirectoryItem is not null &&
        CurrentPath != "/";

    public bool CanGenerateLink => _session is not null && SelectedDirectoryItem is not null;

    public bool CanAddSelectedItemToFavorites =>
        _session is not null &&
        _quickLinkLibraryService is not null &&
        SelectedDirectoryItem is not null;

    public bool CanRemoveSelectedFavorite =>
        _quickLinkLibraryService is not null &&
        SelectedSidebarItem?.IsFavorite == true &&
        !string.IsNullOrWhiteSpace(SelectedSidebarItem.LinkId);

    public bool CanCopySelectedPath => _session is not null && SelectedDirectoryItem is not null;

    public bool CanCopySelectedItem =>
        _session is not null &&
        SelectedDirectoryItem is not null &&
        CurrentPath != "/";

    public bool CanCutSelectedItem =>
        _session is not null &&
        SelectedDirectoryItem is not null &&
        CurrentPath != "/";

    public bool CanPasteClipboard =>
        _session is not null &&
        _fileClipboard is not null &&
        _fileClipboard.Entries.Count > 0 &&
        CurrentPath != "/";

    public bool HasClipboardItems =>
        _fileClipboard is not null &&
        _fileClipboard.Entries.Count > 0;

    public string ClipboardPasteMenuText
    {
        get
        {
            if (_fileClipboard is null || _fileClipboard.Entries.Count == 0)
            {
                return "粘贴";
            }

            var mode = _fileClipboard.Mode == FileClipboardMode.Cut ? "移动" : "复制";
            return $"粘贴 {_fileClipboard.Entries.Count} 项（{mode}）";
        }
    }

    public bool HasActiveTask
    {
        get => _hasActiveTask;
        private set
        {
            if (SetProperty(ref _hasActiveTask, value))
            {
                OnPropertyChanged(nameof(CanCancelActiveTask));
            }
        }
    }

    public bool CanCancelActiveTask =>
        _fileTaskService is not null &&
        HasActiveTask &&
        !string.IsNullOrWhiteSpace(_activeTaskId);

    public string ActiveTaskSummary
    {
        get => _activeTaskSummary;
        private set => SetProperty(ref _activeTaskSummary, value);
    }

    public double ActiveTaskProgress
    {
        get => _activeTaskProgress;
        private set => SetProperty(ref _activeTaskProgress, value);
    }

    public string BootstrapStatus
    {
        get => _bootstrapStatus;
        private set => SetProperty(ref _bootstrapStatus, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusText);

    public void ShowStatus(string message)
    {
        StatusText = message;
    }

    public string CurrentPath
    {
        get => _currentPath;
        private set
        {
            if (SetProperty(ref _currentPath, value))
            {
                OnPropertyChanged(nameof(BreadcrumbText));
                OnPropertyChanged(nameof(StatusPathText));
            }
        }
    }

    public string BreadcrumbText => BuildBreadcrumbText(CurrentPath);

    public string StatusPathText => BuildStatusPathText();

    public string ConnectionSummary
    {
        get => _connectionSummary;
        private set => SetProperty(ref _connectionSummary, value);
    }

    public string DirectorySummary
    {
        get => _directorySummary;
        private set => SetProperty(ref _directorySummary, value);
    }

    public string PreviewDirectorySummary => BuildPreviewDirectorySummary();

    public string SelectedProfileSummary
    {
        get => _selectedProfileSummary;
        private set => SetProperty(ref _selectedProfileSummary, value);
    }

    public string UserMenuTitle
    {
        get => _userMenuTitle;
        private set => SetProperty(ref _userMenuTitle, value);
    }

    public string ManualUsername
    {
        get => _manualUsername;
        set
        {
            if (!SetProperty(ref _manualUsername, value))
            {
                return;
            }

            RefreshConnectActions();
        }
    }

    public string ManualConnectHint
    {
        get => _manualConnectHint;
        private set => SetProperty(ref _manualConnectHint, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value))
            {
                return;
            }

            ApplyDirectoryFilter(keepExistingSelection: true);
        }
    }

    public string GeneratedLink
    {
        get => _generatedLink;
        private set => SetProperty(ref _generatedLink, value);
    }

    public bool RememberPassword
    {
        get => _rememberPassword;
        set
        {
            if (!SetProperty(ref _rememberPassword, value))
            {
                return;
            }

            if (!value && AutoLogin)
            {
                AutoLogin = false;
            }
        }
    }

    public bool AutoLogin
    {
        get => _autoLogin;
        set
        {
            if (!SetProperty(ref _autoLogin, value))
            {
                return;
            }

            if (value)
            {
                RememberPassword = true;
            }
        }
    }

    public PreviewPaneState PreviewPane
    {
        get => _previewPane;
        private set
        {
            if (!SetProperty(ref _previewPane, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasPreviewImage));
            OnPropertyChanged(nameof(IsPreviewLoading));
            OnPropertyChanged(nameof(PreviewIconBrush));
            OnPropertyChanged(nameof(CanPlaySelectedVideo));
        }
    }

    public bool HasPreviewImage => PreviewPane.HasLocalImage;

    public bool IsPreviewLoading => PreviewPane.DisplayState == PreviewDisplayState.Loading;

    public Brush PreviewIconBrush => ResolvePreviewIconBrush(PreviewPane.IconBrushKey);

    public void UpdatePreviewImageMetadata(string localImagePath, uint pixelWidth, uint pixelHeight)
    {
        UpdatePreviewMetadata(
            localImagePath,
            PreviewPane.LocalImagePath,
            "图片",
            pixelWidth,
            pixelHeight
        );
    }

    public void UpdatePreviewVideoMetadata(string localVideoPath, uint pixelWidth, uint pixelHeight)
    {
        UpdatePreviewMetadata(
            localVideoPath,
            PreviewPane.LocalVideoPath,
            "视频",
            pixelWidth,
            pixelHeight
        );
    }

    public void UpdatePreviewPdfMetadata(string localPdfPath, uint pageCount, uint pixelWidth, uint pixelHeight)
    {
        if (string.IsNullOrWhiteSpace(localPdfPath)
            || !string.Equals(PreviewPane.LocalPdfPath, localPdfPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var pageLabel = pageCount > 0 ? $"{pageCount} 页" : "预览";
        if (pixelWidth > 0 && pixelHeight > 0)
        {
            PreviewPane = PreviewPane with { Description = $"PDF · {pageLabel} · {pixelWidth}x{pixelHeight}" };
            return;
        }

        PreviewPane = PreviewPane with { Description = $"PDF · {pageLabel}" };
    }

    private void UpdatePreviewMetadata(
        string localPath,
        string? currentLocalPath,
        string contentTypeLabel,
        uint pixelWidth,
        uint pixelHeight
    )
    {
        if (string.IsNullOrWhiteSpace(localPath)
            || !string.Equals(currentLocalPath, localPath, StringComparison.OrdinalIgnoreCase)
            || pixelWidth == 0
            || pixelHeight == 0)
        {
            return;
        }

        PreviewPane = PreviewPane with { Description = $"{contentTypeLabel} · {pixelWidth}x{pixelHeight}" };
    }

    public bool CanPlaySelectedVideo =>
        _session is not null &&
        SelectedDirectoryItem?.IsDirectory == false &&
        PreviewPane.HasLocalVideo;

    public ServerProfileListItem? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetProperty(ref _selectedProfile, value))
            {
                return;
            }

            ApplySelectedProfileState();
            OnPropertyChanged(nameof(CanLogin));
        }
    }

    public ShareListItem? SelectedShare
    {
        get => _selectedShare;
        set => SetProperty(ref _selectedShare, value);
    }

    public SidebarTab ActiveSidebarTab
    {
        get => _activeSidebarTab;
        private set
        {
            if (!SetProperty(ref _activeSidebarTab, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsShareSidebarActive));
            OnPropertyChanged(nameof(IsFavoriteSidebarActive));
            OnPropertyChanged(nameof(ShareSidebarTabFontWeight));
            OnPropertyChanged(nameof(FavoriteSidebarTabFontWeight));
            OnPropertyChanged(nameof(ShareSidebarTabForeground));
            OnPropertyChanged(nameof(FavoriteSidebarTabForeground));
        }
    }

    public bool IsShareSidebarActive => ActiveSidebarTab == SidebarTab.Shares;

    public bool IsFavoriteSidebarActive => ActiveSidebarTab == SidebarTab.Favorites;

    public FontWeight ShareSidebarTabFontWeight => IsShareSidebarActive
        ? new FontWeight { Weight = 600 }
        : new FontWeight { Weight = 500 };

    public FontWeight FavoriteSidebarTabFontWeight => IsFavoriteSidebarActive
        ? new FontWeight { Weight = 600 }
        : new FontWeight { Weight = 500 };

    public Brush ShareSidebarTabForeground => IsShareSidebarActive
        ? new SolidColorBrush(ColorHelper.FromArgb(255, 24, 34, 48))
        : new SolidColorBrush(ColorHelper.FromArgb(255, 100, 116, 139));

    public Brush FavoriteSidebarTabForeground => IsFavoriteSidebarActive
        ? new SolidColorBrush(ColorHelper.FromArgb(255, 24, 34, 48))
        : new SolidColorBrush(ColorHelper.FromArgb(255, 100, 116, 139));

    public SidebarItemViewModel? SelectedSidebarItem
    {
        get => _selectedSidebarItem;
        set
        {
            if (ReferenceEquals(_selectedSidebarItem, value))
            {
                return;
            }

            _selectedSidebarItem?.SetSelected(false);
            if (!SetProperty(ref _selectedSidebarItem, value))
            {
                return;
            }

            _selectedSidebarItem?.SetSelected(true);
            OnPropertyChanged(nameof(CanRemoveSelectedFavorite));
        }
    }

    public DirectoryItemViewModel? SelectedDirectoryItem
    {
        get => _selectedDirectoryItem;
        set
        {
            if (!SetProperty(ref _selectedDirectoryItem, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanRenameSelectedItem));
            OnPropertyChanged(nameof(CanDeleteSelectedItem));
            OnPropertyChanged(nameof(CanOpenSelectedItem));
            OnPropertyChanged(nameof(CanDownloadSelectedItem));
            OnPropertyChanged(nameof(CanGenerateLink));
            OnPropertyChanged(nameof(CanPlaySelectedVideo));
            OnPropertyChanged(nameof(CanAddSelectedItemToFavorites));
            OnPropertyChanged(nameof(CanCopySelectedPath));
            OnPropertyChanged(nameof(CanCopySelectedItem));
            OnPropertyChanged(nameof(CanCutSelectedItem));
            OnPropertyChanged(nameof(StatusPathText));
            if (value is null)
            {
                _previewEntryService.CancelActivePreview();
                _fileDragDownloadPreparationService?.CancelActive();
            }

            _ = LoadPreviewForSelectionAsync();
        }
    }

    public ObservableCollection<ServerProfileListItem> ServerProfiles { get; } = new();

    public ObservableCollection<ShareListItem> Shares { get; } = new();

    public ObservableCollection<SidebarItemViewModel> SidebarItems { get; } = new();

    public ObservableCollection<DirectoryItemViewModel> DirectoryItems { get; } = new();

    public async Task InitializeAsync()
    {
        await LoadBootstrapAsync();
        await TryAutoLoginAsync();
    }

    public void ShowShareSidebar()
    {
        if (ActiveSidebarTab == SidebarTab.Shares)
        {
            return;
        }

        ActiveSidebarTab = SidebarTab.Shares;
        RebuildSidebarItems();
    }

    public async Task ShowFavoriteSidebarAsync()
    {
        if (ActiveSidebarTab != SidebarTab.Favorites)
        {
            ActiveSidebarTab = SidebarTab.Favorites;
        }

        await LoadQuickLinksAsync();
        RebuildSidebarItems();
    }

    public async Task OpenSelectedSidebarItemAsync()
    {
        if (SelectedSidebarItem is null)
        {
            return;
        }

        if (SelectedSidebarItem.Share is not null)
        {
            await OpenShareAsync(SelectedSidebarItem.Share);
            return;
        }

        if (SelectedSidebarItem.DirectoryItem is not null)
        {
            await OpenPathAsync(SelectedSidebarItem.DirectoryItem.DisplayPath, keepExistingSelection: false);
            return;
        }

        if (SelectedSidebarItem.QuickLink is not null)
        {
            await OpenQuickLinkAsync(SelectedSidebarItem.QuickLink);
        }
    }

    public async Task OpenSidebarItemAsync(SidebarItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedSidebarItem = item;
        await OpenSelectedSidebarItemAsync();
    }

    public async Task OpenAndToggleSidebarItemAsync(SidebarItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedSidebarItem = item;
        if (item.IsDirectory)
        {
            await ToggleSidebarItemExpansionAsync(item);
        }

        await OpenSelectedSidebarItemAsync();
    }

    public async Task ToggleSelectedSidebarItemExpansionAsync()
    {
        if (ActiveSidebarTab != SidebarTab.Shares || SelectedSidebarItem?.IsDirectory != true)
        {
            return;
        }

        await ToggleSidebarItemExpansionAsync(SelectedSidebarItem);
    }

    public async Task ToggleSidebarItemExpansionAsync(SidebarItemViewModel? item)
    {
        if (_session is null || item?.IsDirectory != true)
        {
            return;
        }

        var displayPath = WindowsServerSession.NormalizeDisplayPath(item.DisplayPath);
        var itemIndex = SidebarItems.IndexOf(item);
        if (itemIndex < 0)
        {
            return;
        }

        if (_expandedSidebarPaths.Contains(displayPath))
        {
            _expandedSidebarPaths.Remove(displayPath);
            item.SetExpansion(isExpanded: false);
            RemoveSidebarDescendants(itemIndex, item.Depth);
            return;
        }

        _expandedSidebarPaths.Add(displayPath);
        if (!_session.HasCached(displayPath))
        {
            await LoadSidebarDirectoryAsync(displayPath);
        }

        // await 期间用户可能已折叠该节点或列表结构已变（导航/登出/重建）。
        // 重新校验：item 仍在列表、路径仍在展开集合、索引未变，否则放弃插入避免树错乱。
        if (!_expandedSidebarPaths.Contains(displayPath))
        {
            return;
        }
        var currentIndex = SidebarItems.IndexOf(item);
        if (currentIndex < 0 || currentIndex != itemIndex)
        {
            // 索引变了说明列表被重建，直接刷新展开态更安全。
            RebuildSidebarItems();
            return;
        }

        item.SetExpansion(isExpanded: true);
        InsertSidebarChildren(itemIndex, displayPath, item.Depth + 1);
    }

    public async Task ActivateLinkAsync(string rawLink)
    {
        if (string.IsNullOrWhiteSpace(rawLink))
        {
            StatusText = "链接内容为空，无法打开。";
            return;
        }

        _diagnostics.Info($"开始激活链接：{rawLink}");
        IsBusy = true;
        StatusText = "正在解析链接...";

        try
        {
            var result = await _linkActivationService.ActivateAsync(rawLink.Trim());
            _diagnostics.Info($"链接激活结束。成功={result.Succeeded}；摘要={result.Summary}");

            if (!result.Succeeded || result.Activation is null)
            {
                PreviewPane = result.PreviewPane;
                StatusText = result.Summary;
                return;
            }

            if (_session is null)
            {
                _pendingActivation = result.Activation;
                PreviewPane = result.PreviewPane;
                StatusText = "已解析链接，请先连接对应服务器。";
                return;
            }

            if (!CanCurrentSessionOpen(result.Activation))
            {
                _pendingActivation = result.Activation;
                var targetHost = result.Activation.MatchedServer?.Endpoint.Host ?? result.Activation.Target.ServerHost;
                PreviewPane = result.PreviewPane;
                StatusText = $"该链接属于 {targetHost}，请连接对应服务器后再打开。";
                return;
            }

            await OpenActivationAsync(result.Activation, result.PreviewPane);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task GenerateLinkForSelectionAsync()
    {
        if (_session is null || SelectedDirectoryItem is null)
        {
            StatusText = "请先选择要分享的文件或文件夹。";
            return;
        }

        _diagnostics.Info($"开始生成分享链接：{SelectedDirectoryItem.DisplayPath}");
        IsBusy = true;
        StatusText = "正在生成分享链接...";

        try
        {
            var result = await _linkShareService.BuildForItemAsync(_session, SelectedDirectoryItem);
            _diagnostics.Info($"生成分享链接结束。成功={result.Succeeded}；摘要={result.Summary}");

            if (!result.Succeeded || result.Link is null)
            {
                StatusText = result.Summary;
                return;
            }

            GeneratedLink = result.Link.HttpUrl;
            CopyTextToClipboard(result.Link.HttpUrl);
            StatusText = "已复制链接";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task AddSelectedItemToFavoritesAsync()
    {
        if (_session is null || SelectedDirectoryItem is null || _quickLinkLibraryService is null)
        {
            return;
        }

        var item = SelectedDirectoryItem;
        IsBusy = true;
        StatusText = "正在收藏...";

        try
        {
            var result = await _quickLinkLibraryService.SaveForItemAsync(_session, item);
            StatusText = result.Summary;
            await LoadQuickLinksAsync(silent: true);
            if (ActiveSidebarTab == SidebarTab.Favorites)
            {
                RebuildSidebarItems();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RemoveSelectedFavoriteAsync()
    {
        if (_quickLinkLibraryService is null
            || SelectedSidebarItem?.QuickLink is not QuickLink link
            || string.IsNullOrWhiteSpace(link.Id))
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在取消收藏...";

        try
        {
            var result = await _quickLinkLibraryService.DeleteAsync(link.Id);
            StatusText = result.Summary;
            _quickLinks = result.Links ?? [];
            RebuildSidebarItems();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void CopySelectedPath()
    {
        if (_session is null)
        {
            StatusText = "请先选择要复制路径的文件或文件夹。";
            return;
        }

        var path = BuildStatusPathText();
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText = "请先选择要复制路径的文件或文件夹。";
            return;
        }

        CopyTextToClipboard(path);
        StatusText = "路径已复制";
    }

    public void HandleExternalLinkReceived(string rawLink)
    {
        _ = ActivateLinkAsync(rawLink);
    }

    public void Logout()
    {
        ResetSessionState();
        StatusText = string.Empty;
    }

    public void TogglePreviewPane()
    {
        IsPreviewPaneVisible = !IsPreviewPaneVisible;
    }

    public async Task LoadBootstrapAsync()
    {
        _diagnostics.Info("开始加载启动信息。");
        IsBusy = true;

        try
        {
            var result = await _bootstrapService.LoadAsync();
            ApplyBootstrapResult(result.Succeeded, result.Snapshot, result.Summary);
            _diagnostics.Info($"启动信息加载完成。成功={result.Succeeded}；摘要={result.Summary}");

            if (result.Succeeded)
            {
                StatusText = string.Empty;
            }
            else
            {
                StatusText = "启动信息加载失败，请检查本地环境后重试。";
            }

            PreviewPane = _previewEntryService.BuildArchitecturePlaceholder();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SetSelectedProfileAsActiveAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        _diagnostics.Info($"开始切换当前服务器：{SelectedProfile.DisplayName} ({SelectedProfile.Id})");
        IsBusy = true;
        StatusText = $"正在切换当前服务器：{SelectedProfile.DisplayName}...";

        try
        {
            var result = await _bootstrapService.SetActiveProfileAsync(SelectedProfile.Id);
            ApplyBootstrapResult(result.Succeeded, result.Snapshot, result.Summary);
            _diagnostics.Info($"切换当前服务器结束。成功={result.Succeeded}；摘要={result.Summary}");

            StatusText = result.Succeeded
                ? $"当前服务器已切换为：{SelectedProfile.DisplayName}。"
                : "切换当前服务器失败。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveServerProfileAsync(string? id, string displayName, string host, bool setActive)
    {
        IsBusy = true;
        StatusText = "正在保存服务器设置...";
        try
        {
            var existingUsername = string.IsNullOrWhiteSpace(id)
                ? null
                : _bootstrapSnapshot?.ServerProfiles
                    .FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase))
                    ?.Username;
            var result = await _serverProfileManagementService.SaveProfileAsync(
                new ServerProfileDraft(
                    id,
                    displayName,
                    host,
                    existingUsername,
                    "username_password",
                    "smb3_preferred",
                    setActive
                )
            );

            StatusText = result.Summary;
            if (result.Succeeded)
            {
                await LoadBootstrapAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteServerProfileAsync(string id)
    {
        if (_session is not null
            && string.Equals(_session.Profile.Id, id, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = "请先退出登录";
            return;
        }

        IsBusy = true;
        StatusText = "正在删除服务器...";
        try
        {
            var result = await _serverProfileManagementService.DeleteProfileAsync(id);
            StatusText = result.Summary;
            if (result.Succeeded)
            {
                ApplyBootstrapResult(true, result.Snapshot, result.Summary);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoginAsync()
    {
        if (SelectedProfile is null)
        {
            StatusText = "请先添加服务器";
            return;
        }

        if (CanConnectWithCredentials)
        {
            await ConnectWithCredentialsAsync();
            return;
        }

        if (CanConnectSelectedProfile)
        {
            await ConnectSelectedProfileAsync();
            return;
        }

        StatusText = SelectedProfile.HasStoredCredential
            ? "请输入密码"
            : "请输入账号和密码。";
    }

    public async Task ConnectSelectedProfileAsync()
    {
        if (SelectedProfile is null || !SelectedProfile.HasStoredCredential)
        {
            StatusText = "当前选中的服务器还没有已保存的凭据。";
            _diagnostics.Info("用户尝试连接服务器，但当前没有可用的已保存凭据。");
            return;
        }

        _diagnostics.Info($"开始连接服务器：{SelectedProfile.DisplayName} ({SelectedProfile.Host})");
        IsBusy = true;
        StatusText = $"正在使用已保存的凭据连接：{SelectedProfile.DisplayName}...";

        try
        {
            var result = await _smbSessionService.ConnectStoredProfileAsync(SelectedProfile);
            _diagnostics.Info($"服务器连接结束。成功={result.Succeeded}；摘要={result.Summary}");
            if (!result.Succeeded || result.Session is null)
            {
                ConnectionSummary = "连接失败";
                DirectorySummary = result.Summary;
                StatusText = result.Summary;
                return;
            }

            var session = result.Session;
            ReplaceSession(session);
            OnPropertyChanged(nameof(CanGenerateLink));
            if (result.Snapshot is not null)
            {
                ApplyBootstrapResult(true, result.Snapshot, result.Summary);
            }
            ConnectionSummary = result.Summary;
            UserMenuTitle = string.IsNullOrWhiteSpace(session.Profile.Username)
                ? "用户"
                : session.Profile.Username.Trim();
            PopulateShares(session.Shares);
            await LoadQuickLinksAsync(silent: true);
            RebuildSidebarItems();
            IsWorkspaceVisible = true;
            _diagnostics.Info($"连接成功后已加载共享列表，共 {session.Shares.Count} 个共享。");
            await OpenPathAsync("/", keepExistingSelection: false);
            await ConsumePendingActivationIfPossibleAsync();
            StatusText = $"已连接 {session.Host}，发现 {session.Shares.Count} 个共享";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SetManualPassword(string password)
    {
        if (password == _manualPassword)
        {
            return;
        }

        _manualPassword = password;
        RefreshConnectActions();
    }

    public async Task ConnectWithCredentialsAsync()
    {
        if (SelectedProfile is null)
        {
            StatusText = "请先选择服务器。";
            return;
        }

        if (string.IsNullOrWhiteSpace(ManualUsername) || string.IsNullOrWhiteSpace(_manualPassword))
        {
            StatusText = "请输入账号和密码。";
            return;
        }

        _diagnostics.Info($"开始使用账号密码连接服务器：{SelectedProfile.DisplayName} ({SelectedProfile.Host})");
        IsBusy = true;
        StatusText = $"正在使用账号密码连接：{SelectedProfile.DisplayName}...";

        try
        {
            var result = await _smbSessionService.ConnectWithCredentialsAsync(
                SelectedProfile,
                ManualUsername.Trim(),
                _manualPassword,
                RememberPassword,
                AutoLogin
            );

            _diagnostics.Info($"账号密码连接结束。成功={result.Succeeded}；摘要={result.Summary}");

            if (!result.Succeeded || result.Session is null)
            {
                ConnectionSummary = "连接失败";
                DirectorySummary = result.Summary;
                StatusText = result.Summary;
                return;
            }

            var session = result.Session;
            ReplaceSession(session);
            OnPropertyChanged(nameof(CanGenerateLink));
            if (result.Snapshot is not null)
            {
                ApplyBootstrapResult(true, result.Snapshot, result.Summary);
            }
            ConnectionSummary = result.Summary;
            UserMenuTitle = ManualUsername.Trim();
            PopulateShares(session.Shares);
            await LoadQuickLinksAsync(silent: true);
            RebuildSidebarItems();
            IsWorkspaceVisible = true;
            _manualPassword = string.Empty;
            RefreshConnectActions();
            ManualConnectHint = string.Empty;
            await OpenPathAsync("/", keepExistingSelection: false);
            await ConsumePendingActivationIfPossibleAsync();
            StatusText = $"已连接 {session.Host}，发现 {session.Shares.Count} 个共享";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task OpenShareAsync(ShareListItem? share)
    {
        if (share is null)
        {
            return;
        }

        _diagnostics.Info($"打开共享：{share.Name}");
        SelectedShare = share;
        await OpenPathAsync("/" + share.Name, keepExistingSelection: false);
    }

    public async Task OpenShareRootAsync()
    {
        await OpenPathAsync("/", keepExistingSelection: false);
    }

    public async Task OpenSelectedDirectoryAsync()
    {
        if (SelectedDirectoryItem is null || !SelectedDirectoryItem.IsDirectory)
        {
            return;
        }

        _diagnostics.Info($"打开目录：{SelectedDirectoryItem.DisplayPath}");
        await OpenPathAsync(SelectedDirectoryItem.DisplayPath, keepExistingSelection: false);
    }

    public async Task OpenSelectedItemAsync()
    {
        if (_session is null || SelectedDirectoryItem is null)
        {
            return;
        }

        if (SelectedDirectoryItem.IsDirectory)
        {
            await OpenSelectedDirectoryAsync();
            return;
        }

        _diagnostics.Info($"打开文件：{SelectedDirectoryItem.DisplayPath}");
        IsBusy = true;
        StatusText = $"正在打开文件：{SelectedDirectoryItem.Name}...";

        try
        {
            var result = await _fileOpenService.OpenAsync(_session, SelectedDirectoryItem);
            StatusText = result.Summary;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task PlaySelectedVideoAsync()
    {
        if (!CanPlaySelectedVideo || SelectedDirectoryItem is null)
        {
            return;
        }

        IsBusy = true;
        StatusText = $"正在播放：{SelectedDirectoryItem.Name}...";
        try
        {
            var result = await _fileOpenService.OpenLocalAsync(
                PreviewPane.LocalVideoPath!,
                SelectedDirectoryItem.Name
            );
            StatusText = result.Summary;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DownloadSelectedItemAsync(string localPath)
    {
        if (_session is null || SelectedDirectoryItem is null)
        {
            return;
        }

        var item = SelectedDirectoryItem;
        IsBusy = true;
        StatusText = item.IsDirectory
            ? $"正在下载文件夹：{item.Name}..."
            : $"正在下载文件：{item.Name}...";

        try
        {
            var result = item.IsDirectory
                ? await _fileDownloadService.DownloadDirectoryAsync(_session, item, localPath)
                : await _fileDownloadService.DownloadAsync(_session, item, localPath);
            StatusText = result.Summary;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DownloadSelectedItemsAsync(
        IReadOnlyList<DirectoryItemViewModel> items,
        string localParentDirectory
    )
    {
        if (_session is null || items.Count == 0)
        {
            return;
        }

        IsBusy = true;
        StatusText = $"正在下载 {items.Count} 个项目...";

        try
        {
            var downloadTask = _fileTaskService?.Start(
                "download",
                $"正在下载 {items.Count} 个项目",
                items.Count
            );
            var result = _fileBatchOperationService is not null
                ? await _fileBatchOperationService.DownloadItemsAsync(
                    _session,
                    items,
                    localParentDirectory,
                    downloadTask
                )
                : await DownloadItemsWithoutBatchServiceAsync(
                    _session,
                    items,
                    localParentDirectory,
                    downloadTask
                );

            StatusText = result.Summary;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<FileDownloadResult> PrepareSelectedItemForDragDownloadAsync()
    {
        if (_session is null || SelectedDirectoryItem is null)
        {
            return new FileDownloadResult(
                false,
                "请先选择要拖出的文件。",
                null,
                ErrorCode: "download.no_selection"
            );
        }

        var item = SelectedDirectoryItem;
        IsBusy = true;
        StatusText = item.IsDirectory
            ? $"正在准备拖出文件夹：{item.Name}..."
            : $"正在准备拖出文件：{item.Name}...";

        try
        {
            var result = item.IsDirectory
                ? await _fileDownloadService.PrepareDragDownloadDirectoryAsync(_session, item)
                : await _fileDownloadService.PrepareDragDownloadAsync(_session, item);
            StatusText = result.Succeeded
                ? $"{(item.IsDirectory ? "文件夹" : "文件")}已准备好，可拖出到本地：{item.Name}"
                : result.Summary;
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<FileDragDownloadPreparationResult> PrepareItemsForDragDownloadAsync(
        IReadOnlyList<DirectoryItemViewModel> items
    )
    {
        if (_session is null || items.Count == 0)
        {
            return new FileDragDownloadPreparationResult(
                false,
                "请先选择要拖出的文件或文件夹。",
                [],
                "download.no_selection"
            );
        }

        if (_fileDragDownloadPreparationService is null)
        {
            return new FileDragDownloadPreparationResult(
                false,
                "拖出下载准备服务不可用。",
                [],
                "download.drag_service_unavailable"
            );
        }

        try
        {
            // 异步等待拖出准备完成，避免在 UI 线程上同步阻塞（大文件/目录下载会冻结界面）。
            var result = await _fileDragDownloadPreparationService
                .PrepareManyAsync(_session, items, CancellationToken.None)
                .ConfigureAwait(true);
            if (!result.Succeeded)
            {
                StatusText = result.Summary;
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消拖出准备。";
            return new FileDragDownloadPreparationResult(
                false,
                "已取消拖出准备。",
                [],
                "download.cancelled"
            );
        }
    }

    private async Task PrepareDragDownloadForSelectionAsync(bool automatic = true)
    {
        if (_session is null
            || SelectedDirectoryItem is null
            || _fileDragDownloadPreparationService is null)
        {
            return;
        }

        var item = SelectedDirectoryItem;
        try
        {
            await _fileDragDownloadPreparationService.PrepareAsync(_session, item, automatic: automatic);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex) || ex is IOException or UnauthorizedAccessException)
        {
            _diagnostics.Error(ex, $"拖出下载预热失败：{item.DisplayPath}");
        }
    }

    public async Task NavigateUpAsync()
    {
        if (_session is null)
        {
            return;
        }

        _diagnostics.Info($"返回上级目录：当前路径={_session.CurrentDisplayPath}");
        await OpenPathAsync(
            WindowsServerSession.GetParentDisplayPath(_session.CurrentDisplayPath),
            keepExistingSelection: false
        );
    }

    public async Task RefreshCurrentDirectoryAsync()
    {
        if (_session is null)
        {
            return;
        }

        _diagnostics.Info($"刷新目录：{_session.CurrentDisplayPath}");
        await OpenPathAsync(_session.CurrentDisplayPath, keepExistingSelection: true, forceReload: true);
    }

    public async Task CreateDirectoryAsync(string folderName)
    {
        if (_session is null)
        {
            return;
        }

        IsBusy = true;
        StatusText = $"正在创建文件夹：{folderName}...";

        try
        {
            var result = await _fileWriteService.CreateDirectoryAsync(_session, CurrentPath, folderName);
            StatusText = result.Summary;

            if (result.Succeeded)
            {
                await OpenPathAsync(CurrentPath, keepExistingSelection: false, forceReload: true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<IReadOnlyList<FileUploadConflict>> FindUploadConflictsAsync(
        IReadOnlyList<string> localPaths,
        string? targetDisplayPath = null
    )
    {
        if (_session is null || localPaths.Count == 0)
        {
            return [];
        }

        // FileWriteService conflict probing covers both files and directories.
        return await _fileWriteService.FindUploadConflictsAsync(
            _session,
            targetDisplayPath ?? CurrentPath,
            localPaths
        );
    }

    public async Task UploadFilesAsync(
        IReadOnlyList<string> localPaths,
        IReadOnlyDictionary<string, UploadConflictDecision>? conflictDecisions = null
    )
    {
        await UploadFilesToPathAsync(CurrentPath, localPaths, conflictDecisions);
    }

    public async Task UploadFilesToPathAsync(
        string targetDisplayPath,
        IReadOnlyList<string> localPaths,
        IReadOnlyDictionary<string, UploadConflictDecision>? conflictDecisions = null
    )
    {
        if (_session is null || localPaths.Count == 0)
        {
            return;
        }

        var normalizedTarget = WindowsServerSession.NormalizeDisplayPath(targetDisplayPath);
        IsBusy = true;
        StatusText = localPaths.Count == 1
            ? $"正在上传：{Path.GetFileName(localPaths[0])}..."
            : $"正在上传 {localPaths.Count} 个项目...";

        try
        {
            var uploadTask = _fileTaskService?.Start(
                "upload",
                localPaths.Count == 1
                    ? $"正在上传：{Path.GetFileName(localPaths[0])}"
                    : $"正在上传 {localPaths.Count} 个项目",
                null
            );
            var result = _fileBatchOperationService is not null
                ? await _fileBatchOperationService.UploadLocalPathsAsync(
                    _session,
                    normalizedTarget,
                    localPaths,
                    conflictDecisions,
                    uploadTask
                )
                : ConvertFileOperationResult(await _fileWriteService.UploadFilesAsync(
                    _session,
                    normalizedTarget,
                    localPaths,
                    conflictDecisions,
                    uploadTask
                ));
            StatusText = result.Summary;

            if (result.Succeeded)
            {
                _session.InvalidateDirectory(normalizedTarget);
                if (string.Equals(normalizedTarget, CurrentPath, StringComparison.OrdinalIgnoreCase))
                {
                    await OpenPathAsync(CurrentPath, keepExistingSelection: false, forceReload: true);
                }
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void CopySelectedItem()
    {
        if (SelectedDirectoryItem is null)
        {
            StatusText = "请先选择要复制的文件或文件夹。";
            return;
        }

        _fileClipboard = new FileClipboardState(
            FileClipboardMode.Copy,
            [FileClipboardEntry.FromDirectoryItem(SelectedDirectoryItem)]
        );
        StatusText = "已复制";
        RefreshClipboardProperties();
    }

    public void CopySelectedItems(IReadOnlyList<DirectoryItemViewModel> items)
    {
        SetClipboardItems(FileClipboardMode.Copy, items);
    }

    public void CutSelectedItem()
    {
        if (SelectedDirectoryItem is null)
        {
            StatusText = "请先选择要剪切的文件或文件夹。";
            return;
        }

        _fileClipboard = new FileClipboardState(
            FileClipboardMode.Cut,
            [FileClipboardEntry.FromDirectoryItem(SelectedDirectoryItem)]
        );
        StatusText = "已剪切";
        RefreshClipboardProperties();
    }

    public void CutSelectedItems(IReadOnlyList<DirectoryItemViewModel> items)
    {
        SetClipboardItems(FileClipboardMode.Cut, items);
    }

    public async Task<IReadOnlyList<FilePasteConflict>> FindPasteConflictsAsync()
    {
        if (_session is null || _fileClipboard is null)
        {
            return [];
        }

        return await _fileWriteService.FindPasteConflictsAsync(
            _session,
            CurrentPath,
            _fileClipboard
        );
    }

    public async Task<IReadOnlyList<FilePasteConflict>> FindMoveConflictsAsync(
        IReadOnlyList<DirectoryItemViewModel> items,
        string targetDisplayPath
    )
    {
        if (_session is null || items.Count == 0)
        {
            return [];
        }

        var clipboard = new FileClipboardState(
            FileClipboardMode.Cut,
            items.Select(FileClipboardEntry.FromDirectoryItem).ToArray()
        );
        return await _fileWriteService.FindPasteConflictsAsync(
            _session,
            targetDisplayPath,
            clipboard
        );
    }

    public async Task MoveItemsToPathAsync(
        IReadOnlyList<DirectoryItemViewModel> items,
        string targetDisplayPath,
        IReadOnlyDictionary<string, UploadConflictDecision>? conflictDecisions = null
    )
    {
        if (_session is null || items.Count == 0)
        {
            return;
        }

        var normalizedTarget = WindowsServerSession.NormalizeDisplayPath(targetDisplayPath);
        if (string.Equals(normalizedTarget, CurrentPath, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = "已取消移动。";
            return;
        }

        var clipboard = new FileClipboardState(
            FileClipboardMode.Cut,
            items.Select(FileClipboardEntry.FromDirectoryItem).ToArray()
        );

        IsBusy = true;
        StatusText = items.Count == 1
            ? $"正在移动：{items[0].Name}..."
            : $"正在移动 {items.Count} 项...";

        try
        {
            var result = await _fileWriteService.PasteAsync(
                _session,
                normalizedTarget,
                clipboard,
                conflictDecisions
            );
            StatusText = result.Summary;

            if (result.Succeeded)
            {
                foreach (var entry in clipboard.Entries)
                {
                    _session.InvalidateDirectory(WindowsServerSession.GetParentDisplayPath(entry.DisplayPath));
                }
                _session.InvalidateDirectory(normalizedTarget);
                await OpenPathAsync(CurrentPath, keepExistingSelection: false, forceReload: true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task PasteClipboardAsync(
        IReadOnlyDictionary<string, UploadConflictDecision>? conflictDecisions = null
    )
    {
        if (_session is null || _fileClipboard is null)
        {
            StatusText = "剪贴板中没有可粘贴的项目。";
            return;
        }

        var clipboard = _fileClipboard;
        IsBusy = true;
        StatusText = clipboard.Mode == FileClipboardMode.Copy
            ? "正在复制..."
            : "正在移动...";

        try
        {
            var result = await _fileWriteService.PasteAsync(
                _session,
                CurrentPath,
                clipboard,
                conflictDecisions
            );
            StatusText = result.Summary;

            if (result.Succeeded)
            {
                foreach (var entry in clipboard.Entries)
                {
                    _session.InvalidateDirectory(WindowsServerSession.GetParentDisplayPath(entry.DisplayPath));
                }

                if (clipboard.Mode == FileClipboardMode.Cut)
                {
                    _fileClipboard = null;
                    RefreshClipboardProperties();
                }

                await OpenPathAsync(CurrentPath, keepExistingSelection: false, forceReload: true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RenameSelectedItemAsync(string newName)
    {
        if (_session is null || SelectedDirectoryItem is null)
        {
            return;
        }

        IsBusy = true;
        StatusText = $"正在重命名：{SelectedDirectoryItem.Name}...";

        try
        {
            var result = await _fileWriteService.RenameAsync(_session, SelectedDirectoryItem, newName);
            StatusText = result.Summary;

            if (result.Succeeded)
            {
                await OpenPathAsync(CurrentPath, keepExistingSelection: false, forceReload: true);
                SelectedDirectoryItem = DirectoryItems.FirstOrDefault(item =>
                    string.Equals(item.Name, newName, StringComparison.OrdinalIgnoreCase)
                );
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteSelectedItemAsync()
    {
        if (_session is null || SelectedDirectoryItem is null)
        {
            return;
        }

        var item = SelectedDirectoryItem;
        IsBusy = true;
        StatusText = $"正在删除：{item.Name}...";

        try
        {
            var result = await _fileWriteService.DeleteAsync(_session, item);
            StatusText = result.Summary;

            if (result.Succeeded)
            {
                await OpenPathAsync(CurrentPath, keepExistingSelection: false, forceReload: true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteSelectedItemsAsync(IReadOnlyList<DirectoryItemViewModel> items)
    {
        if (_session is null || items.Count == 0)
        {
            return;
        }

        if (items.Count == 1)
        {
            SelectedDirectoryItem = items[0];
            await DeleteSelectedItemAsync();
            return;
        }

        IsBusy = true;
        StatusText = $"正在删除 {items.Count} 个项目...";

        try
        {
            var deleteTask = _fileTaskService?.Start(
                "delete",
                $"正在删除 {items.Count} 个项目",
                items.Count
            );
            var result = _fileBatchOperationService is not null
                ? await _fileBatchOperationService.DeleteItemsAsync(
                    _session,
                    items,
                    deleteTask
                )
                : await DeleteItemsWithoutBatchServiceAsync(
                    _session,
                    items,
                    deleteTask
                );
            StatusText = result.Summary;

            if (result.Succeeded || result.SucceededItems > 0 || result.SkippedItems > 0)
            {
                await OpenPathAsync(CurrentPath, keepExistingSelection: false, forceReload: true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyBootstrapResult(bool succeeded, AppBootstrapState? snapshot, string summary)
    {
        _bootstrapSnapshot = snapshot;
        BootstrapStatus = summary;
        PopulateProfiles(snapshot);

        if (!succeeded || snapshot is null)
        {
            SelectedProfileSummary = "启动信息不可用。";
            CanConnectSelectedProfile = false;
            ResetSessionState();
            return;
        }

        var preferredProfileId = snapshot.ActiveServer?.Id ?? SelectedProfile?.Id;
        SelectedProfile = ServerProfiles.FirstOrDefault(profile => profile.Id == preferredProfileId)
            ?? ServerProfiles.FirstOrDefault();

        ApplySelectedProfileState();
    }

    private void PopulateProfiles(AppBootstrapState? snapshot)
    {
        ServerProfiles.Clear();

        if (snapshot is null)
        {
            return;
        }

        foreach (var profile in snapshot.ServerProfiles)
        {
            ServerProfiles.Add(ServerProfileListItem.FromStoredProfile(profile, snapshot.ActiveCredential));
        }
    }

    private void PopulateShares(IReadOnlyList<ShareListItem> shares)
    {
        Shares.Clear();
        foreach (var share in shares)
        {
            Shares.Add(share);
        }

        if (ActiveSidebarTab == SidebarTab.Shares)
        {
            RebuildSidebarItems();
        }
    }

    private async Task LoadQuickLinksAsync(bool silent = false)
    {
        if (_quickLinkLibraryService is null)
        {
            _quickLinks = [];
            return;
        }

        var result = await _quickLinkLibraryService.ListAsync();
        if (result.Succeeded)
        {
            _quickLinks = result.Links ?? [];
            if (!silent)
            {
                StatusText = result.Summary;
            }
            return;
        }

        _quickLinks = [];
        if (!silent)
        {
            StatusText = result.Summary;
        }
    }

    private void RebuildSidebarItems()
    {
        var selectedKey = BuildSelectedSidebarKey();
        SelectedSidebarItem = null;

        SidebarItems.Clear();
        if (ActiveSidebarTab == SidebarTab.Shares)
        {
            foreach (var share in Shares)
            {
                var shareItem = SidebarItemViewModel.FromShare(
                    share,
                    _expandedSidebarPaths.Contains("/" + share.Name)
                );
                SidebarItems.Add(shareItem);
                if (shareItem.IsExpanded)
                {
                    AddSidebarDirectoryChildren(shareItem.DisplayPath, depth: 1);
                }
            }
        }
        else
        {
            foreach (var link in _quickLinks)
            {
                SidebarItems.Add(SidebarItemViewModel.FromQuickLink(link));
            }
        }

        RestoreSidebarSelection(selectedKey);
    }

    private string? BuildSelectedSidebarKey()
    {
        if (ActiveSidebarTab == SidebarTab.Shares && CurrentPath != "/")
        {
            return WindowsServerSession.NormalizeDisplayPath(CurrentPath);
        }

        if (SelectedSidebarItem?.IsFavorite == true)
        {
            return SelectedSidebarItem.LinkId;
        }

        return SelectedSidebarItem?.DisplayPath;
    }

    private void RestoreSidebarSelection(string? selectedKey)
    {
        if (string.IsNullOrWhiteSpace(selectedKey))
        {
            return;
        }

        SelectedSidebarItem = SidebarItems.FirstOrDefault(item =>
            item.IsFavorite
                ? string.Equals(item.LinkId, selectedKey, StringComparison.OrdinalIgnoreCase)
                : string.Equals(
                    WindowsServerSession.NormalizeDisplayPath(item.DisplayPath),
                    WindowsServerSession.NormalizeDisplayPath(selectedKey),
                    StringComparison.OrdinalIgnoreCase
                )
        );
    }

    private async Task OpenQuickLinkAsync(QuickLink link)
    {
        StatusText = "正在打开收藏...";
        await ActivateLinkAsync(link.DeepLinkUrl);
    }

    private void AddSidebarDirectoryChildren(string parentDisplayPath, int depth)
    {
        if (_session is null)
        {
            return;
        }

        foreach (var item in _session.CachedItemsFor(parentDisplayPath).Where(item => item.IsDirectory))
        {
            var normalizedPath = WindowsServerSession.NormalizeDisplayPath(item.DisplayPath);
            var isExpanded = _expandedSidebarPaths.Contains(normalizedPath);
            var canExpand = !_session.HasCached(normalizedPath)
                || _session.CachedItemsFor(normalizedPath).Any(child => child.IsDirectory);
            SidebarItems.Add(SidebarItemViewModel.FromDirectory(item, depth, isExpanded, canExpand));
            if (isExpanded)
            {
                AddSidebarDirectoryChildren(normalizedPath, depth + 1);
            }
        }
    }

    private void InsertSidebarChildren(int parentIndex, string parentDisplayPath, int depth)
    {
        if (_session is null)
        {
            return;
        }

        var insertIndex = parentIndex + 1;
        foreach (var item in _session.CachedItemsFor(parentDisplayPath).Where(item => item.IsDirectory))
        {
            var normalizedPath = WindowsServerSession.NormalizeDisplayPath(item.DisplayPath);
            var isExpanded = _expandedSidebarPaths.Contains(normalizedPath);
            var canExpand = !_session.HasCached(normalizedPath)
                || _session.CachedItemsFor(normalizedPath).Any(child => child.IsDirectory);
            SidebarItems.Insert(insertIndex, SidebarItemViewModel.FromDirectory(item, depth, isExpanded, canExpand));
            insertIndex++;
            if (isExpanded)
            {
                insertIndex = InsertSidebarChildrenRecursive(insertIndex, normalizedPath, depth + 1);
            }
        }
    }

    private int InsertSidebarChildrenRecursive(int insertIndex, string parentDisplayPath, int depth)
    {
        if (_session is null)
        {
            return insertIndex;
        }

        foreach (var item in _session.CachedItemsFor(parentDisplayPath).Where(item => item.IsDirectory))
        {
            var normalizedPath = WindowsServerSession.NormalizeDisplayPath(item.DisplayPath);
            var isExpanded = _expandedSidebarPaths.Contains(normalizedPath);
            var canExpand = !_session.HasCached(normalizedPath)
                || _session.CachedItemsFor(normalizedPath).Any(child => child.IsDirectory);
            SidebarItems.Insert(insertIndex, SidebarItemViewModel.FromDirectory(item, depth, isExpanded, canExpand));
            insertIndex++;
            if (isExpanded)
            {
                insertIndex = InsertSidebarChildrenRecursive(insertIndex, normalizedPath, depth + 1);
            }
        }

        return insertIndex;
    }

    private void RemoveSidebarDescendants(int parentIndex, int parentDepth)
    {
        var removeIndex = parentIndex + 1;
        while (removeIndex < SidebarItems.Count && SidebarItems[removeIndex].Depth > parentDepth)
        {
            SidebarItems.RemoveAt(removeIndex);
        }
    }

    private void SyncSidebarToPath(string displayPath)
    {
        var normalizedPath = WindowsServerSession.NormalizeDisplayPath(displayPath);
        SyncSelectedShareFromPath(selectCurrentSidebarPath: false);

        if (_session is null || ActiveSidebarTab != SidebarTab.Shares)
        {
            return;
        }

        if (normalizedPath == "/")
        {
            SelectedSidebarItem = null;
            return;
        }

        ExpandSidebarAncestors(normalizedPath);
        EnsureSidebarAncestorsVisible(normalizedPath);
        RefreshExpandedSidebarItem(normalizedPath);
        SelectSidebarPath(normalizedPath);
    }

    private void EnsureSidebarAncestorsVisible(string displayPath)
    {
        var chain = BuildSidebarPathChain(displayPath);
        for (var index = 0; index < chain.Count - 1; index++)
        {
            var item = FindSidebarItemByPath(chain[index]);
            if (item is null)
            {
                return;
            }

            ExpandSidebarItemFromCache(item);
        }
    }

    private void ExpandSidebarItemFromCache(SidebarItemViewModel item)
    {
        if (_session is null || !item.IsDirectory)
        {
            return;
        }

        var displayPath = WindowsServerSession.NormalizeDisplayPath(item.DisplayPath);
        _expandedSidebarPaths.Add(displayPath);
        var canExpand = !_session.HasCached(displayPath)
            || _session.CachedItemsFor(displayPath).Any(child => child.IsDirectory);
        item.SetExpansion(isExpanded: true, canExpand);

        if (!_session.HasCached(displayPath))
        {
            return;
        }

        var itemIndex = SidebarItems.IndexOf(item);
        if (itemIndex < 0 || HasVisibleSidebarChildren(itemIndex, item.Depth))
        {
            return;
        }

        InsertSidebarChildren(itemIndex, displayPath, item.Depth + 1);
    }

    private void RefreshExpandedSidebarItem(string displayPath)
    {
        if (_session is null)
        {
            return;
        }

        var normalizedPath = WindowsServerSession.NormalizeDisplayPath(displayPath);
        if (!_expandedSidebarPaths.Contains(normalizedPath))
        {
            return;
        }

        var item = FindSidebarItemByPath(normalizedPath);
        if (item is null)
        {
            return;
        }

        var itemIndex = SidebarItems.IndexOf(item);
        if (itemIndex < 0)
        {
            return;
        }

        var canExpand = !_session.HasCached(normalizedPath)
            || _session.CachedItemsFor(normalizedPath).Any(child => child.IsDirectory);
        item.SetExpansion(isExpanded: true, canExpand);
        RemoveSidebarDescendants(itemIndex, item.Depth);
        InsertSidebarChildren(itemIndex, normalizedPath, item.Depth + 1);
    }

    private void SelectSidebarPath(string displayPath)
    {
        var normalizedPath = WindowsServerSession.NormalizeDisplayPath(displayPath);
        SelectedSidebarItem = FindSidebarItemByPath(normalizedPath)
            ?? FindSidebarItemByPath(FirstDisplayPathSegment(normalizedPath));
    }

    private SidebarItemViewModel? FindSidebarItemByPath(string displayPath)
    {
        var normalizedPath = WindowsServerSession.NormalizeDisplayPath(displayPath);
        return SidebarItems.FirstOrDefault(item =>
            !item.IsFavorite
            && string.Equals(
                WindowsServerSession.NormalizeDisplayPath(item.DisplayPath),
                normalizedPath,
                StringComparison.OrdinalIgnoreCase
            )
        );
    }

    private static IReadOnlyList<string> BuildSidebarPathChain(string displayPath)
    {
        var normalizedPath = WindowsServerSession.NormalizeDisplayPath(displayPath);
        if (normalizedPath == "/")
        {
            return [];
        }

        var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var chain = new List<string>(parts.Length);
        for (var index = 0; index < parts.Length; index++)
        {
            chain.Add("/" + string.Join("/", parts.Take(index + 1)));
        }

        return chain;
    }

    private static string FirstDisplayPathSegment(string displayPath)
    {
        var normalizedPath = WindowsServerSession.NormalizeDisplayPath(displayPath);
        var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "/" : "/" + parts[0];
    }

    private bool HasVisibleSidebarChildren(int parentIndex, int parentDepth)
    {
        return parentIndex + 1 < SidebarItems.Count
            && SidebarItems[parentIndex + 1].Depth > parentDepth;
    }

    private async Task LoadSidebarDirectoryAsync(string displayPath)
    {
        if (_session is null)
        {
            return;
        }

        var normalizedPath = WindowsServerSession.NormalizeDisplayPath(displayPath);
        if (_session.HasCached(normalizedPath))
        {
            return;
        }

        StatusText = "正在读取目录...";
        var result = await _directoryBrowserService.LoadAsync(_session, normalizedPath);
        if (result.Succeeded)
        {
            _session.CacheDirectory(normalizedPath, result.Items);
            StatusText = $"已读取 {result.Items.Count} 项";
        }
        else
        {
            StatusText = result.Summary;
        }
    }

    private void ExpandSidebarAncestors(string displayPath)
    {
        var normalized = WindowsServerSession.GetParentDisplayPath(displayPath);
        while (normalized != "/")
        {
            _expandedSidebarPaths.Add(normalized);
            normalized = WindowsServerSession.GetParentDisplayPath(normalized);
        }
    }

    private async Task OpenPathAsync(
        string displayPath,
        bool keepExistingSelection,
        bool forceReload = false
    )
    {
        if (_session is null)
        {
            StatusText = "请先连接服务器，再开始浏览。";
            return;
        }

        var normalizedPath = WindowsServerSession.NormalizeDisplayPath(displayPath);
        var requestVersion = Interlocked.Increment(ref _directoryRequestVersion);
        var shouldUseCache = !forceReload && _session.HasCached(normalizedPath);
        _diagnostics.Info($"开始打开路径：{normalizedPath}；使用缓存={shouldUseCache}；强制刷新={forceReload}");

        IsBusy = true;
        StatusText = $"正在加载 {normalizedPath} ...";

        try
        {
            DirectoryBrowseResult result;
            if (shouldUseCache)
            {
                result = new DirectoryBrowseResult(
                    true,
                    _session.CachedItemsFor(normalizedPath),
                    $"已显示 {normalizedPath} 的缓存内容。",
                    null
                );
            }
            else
            {
                result = await _directoryBrowserService.LoadAsync(_session, normalizedPath);
                if (requestVersion != _directoryRequestVersion)
                {
                    _diagnostics.Info($"目录结果已过期，已丢弃：{normalizedPath}");
                    return;
                }

                if (result.Succeeded)
                {
                    _session.CacheDirectory(normalizedPath, result.Items);
                }
            }

            if (requestVersion != _directoryRequestVersion)
            {
                _diagnostics.Info($"目录结果已过期，已丢弃：{normalizedPath}");
                return;
            }

            _diagnostics.Info($"路径打开结束：{normalizedPath}；成功={result.Succeeded}；摘要={result.Summary}");

            if (result.Succeeded)
            {
                ApplyCurrentPath(normalizedPath);
                ApplyDirectoryItems(result.Items, keepExistingSelection);
                SyncSidebarToPath(normalizedPath);
                DirectorySummary = result.Summary;
                StatusText = result.Summary;
            }
            else
            {
                if (_session.HasCached(normalizedPath))
                {
                    var cachedItems = _session.CachedItemsFor(normalizedPath);
                    ApplyCurrentPath(normalizedPath);
                    ApplyDirectoryItems(cachedItems, keepExistingSelection);
                    SyncSidebarToPath(normalizedPath);
                    var fallbackSummary = $"目录加载失败，已显示缓存内容：{normalizedPath}";
                    _diagnostics.Info($"{fallbackSummary}；原始错误={result.Summary}");
                    DirectorySummary = fallbackSummary;
                    StatusText = fallbackSummary;
                    return;
                }

                _diagnostics.Info($"目录加载失败，保留当前内容：{CurrentPath}；目标={normalizedPath}；错误={result.Summary}");
                DirectorySummary = result.Summary;
                StatusText = result.Summary;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyCurrentPath(string displayPath)
    {
        if (_session is null)
        {
            return;
        }

        _session.NavigateTo(displayPath);
        CurrentPath = _session.CurrentDisplayPath;
        CanNavigateUp = CurrentPath != "/";
        OnPropertyChanged(nameof(CanCreateDirectory));
        OnPropertyChanged(nameof(CanUploadFiles));
        OnPropertyChanged(nameof(CanRenameSelectedItem));
        OnPropertyChanged(nameof(CanDeleteSelectedItem));
        OnPropertyChanged(nameof(CanDownloadSelectedItem));
        OnPropertyChanged(nameof(CanCopySelectedPath));
        OnPropertyChanged(nameof(CanCopySelectedItem));
        OnPropertyChanged(nameof(CanCutSelectedItem));
        OnPropertyChanged(nameof(CanPasteClipboard));
    }

    private void ApplyDirectoryItems(
        IReadOnlyList<DirectoryItemViewModel> items,
        bool keepExistingSelection
    )
    {
        _currentDirectoryItems = items;
        if (!keepExistingSelection && !string.IsNullOrWhiteSpace(SearchText))
        {
            SearchText = string.Empty;
            return;
        }

        ApplyDirectoryFilter(keepExistingSelection);
    }

    private void ApplyDirectoryFilter(bool keepExistingSelection)
    {
        var priorPath = keepExistingSelection ? SelectedDirectoryItem?.DisplayPath : null;
        var query = SearchText.Trim();
        var filteredItems = string.IsNullOrWhiteSpace(query)
            ? _currentDirectoryItems
            : _currentDirectoryItems
                .Where(item =>
                    item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.DisplayPath.Contains(query, StringComparison.OrdinalIgnoreCase)
                )
                .ToArray();

        DirectoryItems.Clear();
        foreach (var item in filteredItems)
        {
            DirectoryItems.Add(item);
        }

        SelectedDirectoryItem = priorPath is not null
            ? DirectoryItems.FirstOrDefault(item => item.DisplayPath == priorPath)
            : DirectoryItems.FirstOrDefault();

        OnPropertyChanged(nameof(DirectoryItems));
        OnPropertyChanged(nameof(PreviewDirectorySummary));
        OnPropertyChanged(nameof(StatusPathText));
        OnPropertyChanged(nameof(CanCreateDirectory));
        OnPropertyChanged(nameof(CanUploadFiles));
        OnPropertyChanged(nameof(CanRenameSelectedItem));
        OnPropertyChanged(nameof(CanDeleteSelectedItem));
        OnPropertyChanged(nameof(CanOpenSelectedItem));
        OnPropertyChanged(nameof(CanDownloadSelectedItem));
        OnPropertyChanged(nameof(CanGenerateLink));
        OnPropertyChanged(nameof(CanAddSelectedItemToFavorites));
        OnPropertyChanged(nameof(CanCopySelectedPath));
        OnPropertyChanged(nameof(CanCopySelectedItem));
        OnPropertyChanged(nameof(CanCutSelectedItem));
        OnPropertyChanged(nameof(CanPasteClipboard));
    }

    private void ApplySelectedProfileState()
    {
        // 切换服务器配置时清空已输入的密码，避免把上一个服务器的密码
        // 误用于新服务器（错发凭据 / 认证困惑）。请求 code-behind 同步清空 PasswordBox。
        if (_manualPassword.Length > 0)
        {
            _manualPassword = string.Empty;
            PasswordInputClearRequested?.Invoke();
        }

        if (SelectedProfile is null)
        {
            SelectedProfileSummary = string.Empty;
            UserMenuTitle = "用户";
            CanConnectSelectedProfile = false;
            CanConnectWithCredentials = false;
            return;
        }

        SelectedProfileSummary = SelectedProfile.DisplayName;
        UserMenuTitle = string.IsNullOrWhiteSpace(SelectedProfile.Username)
            ? "用户"
            : SelectedProfile.Username.Trim();
        ManualUsername = SelectedProfile.Username ?? string.Empty;
        ManualConnectHint = string.Empty;
        RefreshConnectActions();
    }

    private string BuildStatusPathText()
    {
        if (_session is null)
        {
            return string.Empty;
        }

        if (SelectedDirectoryItem is not null)
        {
            var remotePath = SelectedDirectoryItem.RemotePath == "/"
                ? string.Empty
                : SelectedDirectoryItem.RemotePath;
            return $"/{SelectedDirectoryItem.ShareName}{remotePath}";
        }

        if (CurrentPath == "/")
        {
            return string.Empty;
        }

        var location = _session.ResolveCurrentLocation();
        if (location is null)
        {
            return CurrentPath;
        }

        var currentRemotePath = location.RemotePath == "/"
            ? string.Empty
            : location.RemotePath;
        return $"/{location.ShareName}{currentRemotePath}";
    }

    private void RefreshClipboardProperties()
    {
        OnPropertyChanged(nameof(CanPasteClipboard));
        OnPropertyChanged(nameof(HasClipboardItems));
        OnPropertyChanged(nameof(ClipboardPasteMenuText));
    }

    private string BuildPreviewDirectorySummary()
    {
        var dirs = DirectoryItems.Count(item => item.IsDirectory);
        var files = DirectoryItems.Count - dirs;
        var totalBytes = DirectoryItems.Aggregate(
            0UL,
            (total, item) => item.IsDirectory ? total : total + (item.SizeBytes ?? 0UL)
        );
        return $"{dirs} 个文件夹 · {files} 个文件 · {FormatBytes(totalBytes)}";
    }

    private static string BuildBreadcrumbText(string path)
    {
        var normalizedPath = WindowsServerSession.NormalizeDisplayPath(path);
        if (normalizedPath == "/")
        {
            return "全部共享";
        }

        return string.Join(
            " / ",
            normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries)
        );
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }

    private void ApplyPreviewForSelection()
    {
        if (SelectedDirectoryItem is null)
        {
            GeneratedLink = string.Empty;
            PreviewPane = _previewEntryService.BuildArchitecturePlaceholder();
            return;
        }

        GeneratedLink = string.Empty;
        PreviewPane = new PreviewPaneState(
            SelectedDirectoryItem.Name,
            BuildPreviewMetaText(SelectedDirectoryItem),
            SelectedDirectoryItem.IsDirectory
                ? string.Empty
                : "正在加载",
            DisplayState: PreviewDisplayState.Loading,
            IconGlyph: SelectedDirectoryItem.IconGlyph,
            IconBrushKey: SelectedDirectoryItem.IsDirectory ? "RynatFolderBrush" : "RynatMutedBrush"
        );
    }

    private string BuildPreviewMetaText(DirectoryItemViewModel item)
    {
        if (item.IsDirectory)
        {
            if (_session?.HasCached(item.DisplayPath) == true)
            {
                return $"{_session.CachedItemsFor(item.DisplayPath).Count} 项 · 文件夹";
            }

            return "文件夹";
        }

        return $"{item.TypeLabel} · {item.SizeLabel} · {item.ModifiedLabel}";
    }

    private static Brush ResolvePreviewIconBrush(string resourceKey)
    {
        if (!string.IsNullOrWhiteSpace(resourceKey)
            && Application.Current.Resources.TryGetValue(resourceKey, out var value)
            && value is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(ColorHelper.FromArgb(255, 100, 116, 139));
    }

    private async Task LoadPreviewForSelectionAsync()
    {
        if (SelectedDirectoryItem is null)
        {
            GeneratedLink = string.Empty;
            PreviewPane = _previewEntryService.BuildArchitecturePlaceholder();
            return;
        }

        var selectedItem = SelectedDirectoryItem;
        var requestVersion = Interlocked.Increment(ref _previewRequestVersion);
        ApplyPreviewForSelection();

        if (_session is null)
        {
            return;
        }

        _diagnostics.Info($"开始加载预览计划：{selectedItem.DisplayPath}");
        var result = await _previewEntryService.LoadForItemAsync(_session, selectedItem);
        if (requestVersion != _previewRequestVersion || !ReferenceEquals(SelectedDirectoryItem, selectedItem))
        {
            _diagnostics.Info($"预览结果已过期，已丢弃：{selectedItem.DisplayPath}");
            return;
        }

        PreviewPane = result.Pane;
        _diagnostics.Info($"预览计划加载结束。成功={result.Succeeded}；摘要={result.Summary}");
    }

    private void SyncSelectedShareFromPath(bool selectCurrentSidebarPath = true)
    {
        if (_session is null)
        {
            SelectedShare = null;
            SelectedSidebarItem = null;
            return;
        }

        var location = _session.ResolveCurrentLocation();
        SelectedShare = location is null
            ? null
            : Shares.FirstOrDefault(share => share.Name == location.ShareName);
        if (ActiveSidebarTab == SidebarTab.Shares)
        {
            if (CurrentPath == "/")
            {
                SelectedSidebarItem = null;
                return;
            }

            if (!selectCurrentSidebarPath)
            {
                return;
            }

            SelectedSidebarItem = SidebarItems.FirstOrDefault(item =>
                item.IsDirectory && string.Equals(item.DisplayPath, CurrentPath, StringComparison.OrdinalIgnoreCase)
            )
            ?? SidebarItems.FirstOrDefault(item =>
                item.IsShare && string.Equals(item.Title, location?.ShareName, StringComparison.OrdinalIgnoreCase)
            );
        }
    }

    private void ResetSessionState()
    {
        // 登出/断开会话前，先取消所有依赖当前会话的后台任务，
        // 避免断开连接后预览/拖拽预热/文件传输仍持有旧 session 引用继续跑到 FFI 失败。
        _previewEntryService.CancelActivePreview();
        _fileDragDownloadPreparationService?.CancelActive();
        _fileTaskService?.CancelAll();
        _activeTaskId = null;

        IsWorkspaceVisible = false;
        ReplaceSession(null);
        Shares.Clear();
        SidebarItems.Clear();
        _quickLinks = [];
        DirectoryItems.Clear();
        _currentDirectoryItems = [];
        SearchText = string.Empty;
        SelectedShare = null;
        SelectedSidebarItem = null;
        ActiveSidebarTab = SidebarTab.Shares;
        SelectedDirectoryItem = null;
        CurrentPath = "/";
        OnPropertyChanged(nameof(PreviewDirectorySummary));
        UserMenuTitle = "用户";
        ConnectionSummary = "尚未连接";
        DirectorySummary = "请先连接服务器，再浏览远端内容。";
        CanNavigateUp = false;
        GeneratedLink = string.Empty;
        _fileClipboard = null;
        OnPropertyChanged(nameof(CanCreateDirectory));
        OnPropertyChanged(nameof(CanUploadFiles));
        OnPropertyChanged(nameof(CanRenameSelectedItem));
        OnPropertyChanged(nameof(CanDeleteSelectedItem));
        OnPropertyChanged(nameof(CanOpenSelectedItem));
        OnPropertyChanged(nameof(CanDownloadSelectedItem));
        OnPropertyChanged(nameof(CanGenerateLink));
        OnPropertyChanged(nameof(CanAddSelectedItemToFavorites));
        OnPropertyChanged(nameof(CanRemoveSelectedFavorite));
        OnPropertyChanged(nameof(CanCopySelectedPath));
        OnPropertyChanged(nameof(CanCopySelectedItem));
        OnPropertyChanged(nameof(CanCutSelectedItem));
        RefreshClipboardProperties();
    }

    private void ReplaceSession(WindowsServerSession? nextSession)
    {
        if (!ReferenceEquals(_session, nextSession))
        {
            _smbSessionService.Disconnect(_session);
        }

        _session = nextSession;
    }

    private void RefreshConnectActions()
    {
        CanConnectSelectedProfile =
            SelectedProfile?.HasStoredCredential == true &&
            string.Equals(
                ManualUsername.Trim(),
                SelectedProfile.Username?.Trim() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase
            );
        CanConnectWithCredentials =
            SelectedProfile is not null &&
            !string.IsNullOrWhiteSpace(ManualUsername) &&
            !string.IsNullOrWhiteSpace(_manualPassword);
        OnPropertyChanged(nameof(CanLogin));
    }

    private async Task TryAutoLoginAsync()
    {
        if (IsWorkspaceVisible
            || _bootstrapSnapshot?.ActiveCredential?.AutoLogin != true
            || SelectedProfile is null
            || !SelectedProfile.HasStoredCredential)
        {
            return;
        }

        IsAutoLoginConnecting = true;
        StatusText = $"正在自动连接：{SelectedProfile.DisplayName}...";
        try
        {
            await ConnectSelectedProfileAsync();
        }
        finally
        {
            IsAutoLoginConnecting = false;
        }
    }

    private void FileTaskService_TaskChanged(object? sender, WindowsFileTaskSnapshot snapshot)
    {
        if (_dispatcherQueue is not null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => ApplyFileTaskSnapshot(snapshot));
            return;
        }

        ApplyFileTaskSnapshot(snapshot);
    }

    private void ApplyFileTaskSnapshot(WindowsFileTaskSnapshot snapshot)
    {
        var activeSnapshot = GetLatestActiveTaskSnapshot()
            ?? (IsActiveTaskSnapshot(snapshot) ? snapshot : null);
        var displaySnapshot = activeSnapshot ?? snapshot;

        _activeTaskId = activeSnapshot?.Id;
        HasActiveTask = activeSnapshot is not null;
        ActiveTaskSummary = displaySnapshot.TotalItems.HasValue && displaySnapshot.TotalItems.Value > 0
            ? $"{displaySnapshot.Summary}（{displaySnapshot.CompletedItems}/{displaySnapshot.TotalItems.Value}）"
            : displaySnapshot.Summary;
        ActiveTaskProgress = displaySnapshot.TotalItems.HasValue && displaySnapshot.TotalItems.Value > 0
            ? Math.Clamp((double)displaySnapshot.CompletedItems / displaySnapshot.TotalItems.Value * 100.0, 0.0, 100.0)
            : 0.0;
        OnPropertyChanged(nameof(CanCancelActiveTask));
    }

    public void CancelActiveTask()
    {
        _previewEntryService.CancelActivePreview();
        _fileDragDownloadPreparationService?.CancelActive();

        if (_fileTaskService is null || string.IsNullOrWhiteSpace(_activeTaskId))
        {
            return;
        }

        if (_fileTaskService.Cancel(_activeTaskId))
        {
            StatusText = "正在取消";
        }
    }

    private WindowsFileTaskSnapshot? GetLatestActiveTaskSnapshot()
    {
        if (_fileTaskService is null)
        {
            return null;
        }

        return _fileTaskService
            .ListSnapshots()
            .FirstOrDefault(IsActiveTaskSnapshot);
    }

    private void SetClipboardItems(
        FileClipboardMode mode,
        IReadOnlyList<DirectoryItemViewModel> items
    )
    {
        if (items.Count == 0)
        {
            StatusText = mode == FileClipboardMode.Copy
                ? "请先选择要复制的文件或文件夹。"
                : "请先选择要剪切的文件或文件夹。";
            return;
        }

        _fileClipboard = new FileClipboardState(
            mode,
            items.Select(FileClipboardEntry.FromDirectoryItem).ToArray()
        );
        StatusText = mode == FileClipboardMode.Copy
            ? (items.Count == 1 ? "已复制" : $"已复制 {items.Count} 项")
            : (items.Count == 1 ? "已剪切" : $"已剪切 {items.Count} 项");
        RefreshClipboardProperties();
    }

    private async Task<FileBatchOperationResult> DownloadItemsWithoutBatchServiceAsync(
        WindowsServerSession session,
        IReadOnlyList<DirectoryItemViewModel> items,
        string localParentDirectory,
        WindowsFileTaskHandle? task
    )
    {
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;
        var errors = new List<string>();
        task?.Start($"正在下载 {items.Count} 个项目...");

        foreach (var item in items)
        {
            task?.CancellationToken.ThrowIfCancellationRequested();
            var result = item.IsDirectory
                ? await _fileDownloadService.DownloadDirectoryAsync(
                    session,
                    item,
                    localParentDirectory,
                    task,
                    task?.CancellationToken ?? CancellationToken.None,
                    updateTaskState: false
                )
                : await _fileDownloadService.DownloadAsync(
                    session,
                    item,
                    Path.Combine(localParentDirectory, SafeLocalFileName(item.Name)),
                    task,
                    task?.CancellationToken ?? CancellationToken.None,
                    updateTaskState: false
                );

            if (result.Succeeded)
            {
                succeeded++;
                skipped += result.SkippedItems;
            }
            else
            {
                failed++;
                errors.Add(result.Summary);
            }

            task?.ReportProgress(succeeded + failed + skipped, items.Count, $"批量下载中：已处理 {succeeded + failed + skipped}/{items.Count} 项。");
        }

        return BuildInlineBatchResult(task, "批量下载", items.Count, succeeded, failed, skipped, errors);
    }

    private async Task<FileBatchOperationResult> DeleteItemsWithoutBatchServiceAsync(
        WindowsServerSession session,
        IReadOnlyList<DirectoryItemViewModel> items,
        WindowsFileTaskHandle? task
    )
    {
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;
        var errors = new List<string>();
        task?.Start($"正在删除 {items.Count} 个项目...");

        foreach (var item in items)
        {
            task?.CancellationToken.ThrowIfCancellationRequested();
            var result = await _fileWriteService.DeleteAsync(
                session,
                item,
                task?.CancellationToken ?? CancellationToken.None
            );

            if (result.Succeeded)
            {
                succeeded++;
            }
            else if (result.ErrorCode?.Contains("skipped", StringComparison.OrdinalIgnoreCase) == true)
            {
                skipped++;
            }
            else
            {
                failed++;
                errors.Add($"{item.Name}：{result.Summary}");
            }

            task?.ReportProgress(succeeded + failed + skipped, items.Count, $"批量删除中：已处理 {succeeded + failed + skipped}/{items.Count} 项。");
        }

        return BuildInlineBatchResult(task, "批量删除", items.Count, succeeded, failed, skipped, errors);
    }

    private static FileBatchOperationResult BuildInlineBatchResult(
        WindowsFileTaskHandle? task,
        string label,
        int requested,
        int succeeded,
        int failed,
        int skipped,
        IReadOnlyList<string> errors
    )
    {
        var summaryParts = new List<string> { $"{label}完成：成功 {succeeded} 项" };
        if (skipped > 0)
        {
            summaryParts.Add($"跳过 {skipped} 项");
        }
        if (failed > 0)
        {
            summaryParts.Add($"失败 {failed} 项");
        }

        var result = new FileBatchOperationResult(
            failed == 0 && succeeded > 0,
            string.Join("，", summaryParts) + "。",
            requested,
            succeeded,
            failed,
            skipped,
            0,
            0,
            errors
        );
        if (result.Succeeded)
        {
            task?.Complete(result.Summary);
        }
        else
        {
            task?.Fail(result.Summary, "batch.failed");
        }

        return result;
    }

    private static string SafeLocalFileName(string name)
    {
        var safeName = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        return string.IsNullOrWhiteSpace(safeName) ? "download" : safeName;
    }

    private static bool IsActiveTaskSnapshot(WindowsFileTaskSnapshot snapshot) =>
        !IsBackgroundDragDownloadPreparation(snapshot) &&
        snapshot.State is WindowsFileTaskState.Pending or WindowsFileTaskState.Running;

    private static bool IsBackgroundDragDownloadPreparation(WindowsFileTaskSnapshot snapshot) =>
        snapshot.Kind.StartsWith("drag_download_prepare", StringComparison.Ordinal);

    private static FileBatchOperationResult ConvertFileOperationResult(FileOperationResult result) =>
        new(
            result.Succeeded,
            result.Summary,
            result.SucceededItems + result.SkippedItems + result.FailedItems,
            result.SucceededItems,
            result.FailedItems > 0 ? result.FailedItems : result.Succeeded ? 0 : 1,
            result.SkippedItems,
            result.ReplacedItems,
            0,
            result.Errors ?? (result.Succeeded ? [] : [result.Summary])
        );

    private async Task ConsumePendingActivationIfPossibleAsync()
    {
        if (_pendingActivation is null || _session is null)
        {
            return;
        }

        if (!CanCurrentSessionOpen(_pendingActivation))
        {
            return;
        }

        var activation = _pendingActivation;
        _pendingActivation = null;
        await OpenActivationAsync(activation, _previewEntryService.BuildForActivation(activation));
    }

    private async Task OpenActivationAsync(LinkActivation activation, PreviewPaneState previewPane)
    {
        var displayPath = BuildDisplayPathForRemote(
            activation.Target.Share,
            activation.BrowseLocation.RemotePath
        );
        var selectedDisplayPath = activation.BrowseLocation.SelectedPath is null
            ? null
            : BuildDisplayPathForRemote(
                activation.Target.Share,
                activation.BrowseLocation.SelectedPath
            );

        await OpenPathAsync(displayPath, keepExistingSelection: false);

        if (!string.IsNullOrWhiteSpace(selectedDisplayPath))
        {
            SelectedDirectoryItem = DirectoryItems.FirstOrDefault(item => item.DisplayPath == selectedDisplayPath);
        }

        PreviewPane = previewPane;
        StatusText = "已根据链接定位到目标内容。";
    }

    private bool CanCurrentSessionOpen(LinkActivation activation)
    {
        if (_session is null)
        {
            return false;
        }

        if (activation.MatchedServer is not null)
        {
            return string.Equals(
                       activation.MatchedServer.Endpoint.Host,
                       _session.Host,
                       StringComparison.OrdinalIgnoreCase
                   )
                   || string.Equals(
                       activation.MatchedServer.Id,
                       _session.Profile.Id,
                       StringComparison.OrdinalIgnoreCase
                   );
        }

        return string.Equals(
            activation.Target.ServerHost,
            _session.Host,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static string BuildDisplayPathForRemote(string share, string remotePath)
    {
        var normalizedRemote = (remotePath ?? string.Empty).Trim();
        var suffix = normalizedRemote == "/" || normalizedRemote.Length == 0
            ? string.Empty
            : "/" + normalizedRemote.Trim('/');
        return "/" + share + suffix;
    }

    private static void CopyTextToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
