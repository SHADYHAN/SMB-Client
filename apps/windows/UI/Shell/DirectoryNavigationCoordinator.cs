using System.Threading;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Services.Directory;
using Rynat.WindowsClient.UI.Files;
using Rynat.WindowsClient.UI.Navigation;
using Rynat.WindowsClient.UI.Preview;
using Rynat.WindowsClient.UI.Status;

namespace Rynat.WindowsClient.UI.Shell;

public sealed class DirectoryNavigationCoordinator
{
    private readonly IDirectoryService _directoryService;
    private readonly FileListViewModel _fileList;
    private readonly NavigationTreeViewModel _navigation;
    private readonly PreviewPaneViewModel _preview;
    private readonly StatusBarViewModel _status;
    private readonly Func<Exception, string, string> _userFacingError;
    private readonly Func<Exception, Task<bool>> _handleSessionIssueAsync;
    private readonly SemaphoreSlim _directoryLoadLock = new(1, 1);
    private string? _loadingDirectoryKey;
    private CancellationTokenSource? _directoryLoadCancellation;
    private long _directoryLoadGeneration;

    public DirectoryNavigationCoordinator(
        IDirectoryService directoryService,
        FileListViewModel fileList,
        NavigationTreeViewModel navigation,
        PreviewPaneViewModel preview,
        StatusBarViewModel status,
        Func<Exception, string, string> userFacingError,
        Func<Exception, Task<bool>> handleSessionIssueAsync
    )
    {
        _directoryService = directoryService;
        _fileList = fileList;
        _navigation = navigation;
        _preview = preview;
        _status = status;
        _userFacingError = userFacingError;
        _handleSessionIssueAsync = handleSessionIssueAsync;
    }

    public string? CurrentShare { get; private set; }

    public string CurrentPath { get; private set; } = "/";

    public bool HasCurrentDirectory => CurrentShare is not null;

    public void Clear()
    {
        CancelActiveDirectoryLoad();
        CurrentShare = null;
        CurrentPath = "/";
    }

    public void ShowShareRoot(ServerSession session, string statusMessage, Action refreshCommands)
    {
        Clear();
        if (_navigation.SelectedNode is not null)
        {
            _navigation.SelectedNode.IsSelected = false;
        }

        _navigation.SelectedNode = null;
        _fileList.ShowShareRoot(session);
        _preview.ShowSelection(null);
        _status.Message = statusMessage;
        refreshCommands();
    }

    public async Task<bool> LoadAsync(
        ServerSession? session,
        string share,
        string path,
        NavigationNodeViewModel? navigationNode,
        bool? expandNavigationNode,
        Action refreshCommands
    )
    {
        if (session is null)
        {
            return false;
        }

        var normalizedKey = $"{share}:{NormalizeDirectoryPath(path)}";
        if (_loadingDirectoryKey == normalizedKey)
        {
            if (navigationNode is not null && expandNavigationNode is bool requestedExpansion)
            {
                navigationNode.IsExpanded = requestedExpansion;
            }

            _status.Message = "目录正在加载...";
            return false;
        }

        if (_loadingDirectoryKey is not null)
        {
            CancelActiveDirectoryLoad();
        }

        await _directoryLoadLock.WaitAsync();
        try
        {
            if (_loadingDirectoryKey == normalizedKey)
            {
                if (navigationNode is not null && expandNavigationNode is bool requestedExpansion)
                {
                    navigationNode.IsExpanded = requestedExpansion;
                }

                return false;
            }

            var loadGeneration = BeginDirectoryLoad(normalizedKey);
            var loadCancellationToken = _directoryLoadCancellation?.Token ?? CancellationToken.None;
            _fileList.IsLoading = true;
            _status.Message = "正在加载目录...";

            try
            {
                var directory = await _directoryService.ListAsync(session, share, path, loadCancellationToken);
                if (!IsCurrentLoad(loadGeneration) || loadCancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                CurrentShare = directory.Share;
                CurrentPath = directory.Path;
                _fileList.ShowDirectory(directory, session.Host);
                _preview.ShowSelection(null);
                refreshCommands();

                if (navigationNode is not null)
                {
                    _navigation.ReplaceChildren(
                        navigationNode,
                        directory.Items.Where(item => item.IsDirectory).ToArray()
                    );
                    if (expandNavigationNode is bool requestedExpansion)
                    {
                        navigationNode.IsExpanded = requestedExpansion;
                    }

                    navigationNode.IsSelected = true;
                    _navigation.SelectedNode = navigationNode;
                }

                _status.Message = $"{directory.Items.Count} 个项目";
                return true;
            }
            catch (OperationCanceledException) when (!IsCurrentLoad(loadGeneration) || loadCancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                if (!IsCurrentLoad(loadGeneration))
                {
                    return false;
                }

                if (await _handleSessionIssueAsync(ex))
                {
                    return false;
                }

                _status.Message = _userFacingError(ex, "目录加载失败");
                return false;
            }
            finally
            {
                if (IsCurrentLoad(loadGeneration))
                {
                    _loadingDirectoryKey = null;
                    _directoryLoadCancellation = null;
                    _fileList.IsLoading = false;
                }
            }
        }
        finally
        {
            _directoryLoadLock.Release();
        }
    }

    public Task<bool> RefreshAsync(ServerSession? session, Action refreshCommands)
    {
        var currentShare = CurrentShare;
        return currentShare is null
            ? Task.FromResult(false)
            : LoadAsync(session, currentShare, CurrentPath, CurrentNavigationNode(), expandNavigationNode: null, refreshCommands);
    }

    public NavigationNodeViewModel? CurrentNavigationNode()
    {
        var selected = _navigation.SelectedNode;
        return selected is not null
            && CurrentShare is not null
            && selected.Share.Equals(CurrentShare, StringComparison.OrdinalIgnoreCase)
            && NormalizeDirectoryPath(selected.Path) == NormalizeDirectoryPath(CurrentPath)
                ? selected
                : null;
    }

    public static string NormalizeDirectoryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        var normalized = path.Replace('\\', '/').Trim();
        return normalized.StartsWith('/') ? normalized : "/" + normalized;
    }

    private long BeginDirectoryLoad(string normalizedKey)
    {
        _directoryLoadCancellation = new CancellationTokenSource();
        _loadingDirectoryKey = normalizedKey;
        return Interlocked.Increment(ref _directoryLoadGeneration);
    }

    private void CancelActiveDirectoryLoad()
    {
        Interlocked.Increment(ref _directoryLoadGeneration);
        _directoryLoadCancellation?.Cancel();
        _directoryLoadCancellation = null;
        _loadingDirectoryKey = null;
        _fileList.IsLoading = false;
    }

    private bool IsCurrentLoad(long generation) =>
        Interlocked.Read(ref _directoryLoadGeneration) == generation;
}
