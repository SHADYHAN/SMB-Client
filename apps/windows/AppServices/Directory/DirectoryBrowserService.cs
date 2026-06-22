using Rynat.Client;
using Rynat.WindowsClient.Infrastructure;
using Rynat.WindowsClient.UI.Main;

namespace Rynat.WindowsClient.AppServices.Directory;

public sealed class DirectoryBrowserService
{
    private readonly RynatCoreBridge _bridge;
    private readonly WindowsClientDiagnostics _diagnostics;

    public DirectoryBrowserService(RynatCoreBridge bridge, WindowsClientDiagnostics diagnostics)
    {
        _bridge = bridge;
        _diagnostics = diagnostics;
    }

    public Task<DirectoryBrowseResult> LoadAsync(
        WindowsServerSession session,
        string displayPath,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedDisplayPath = WindowsServerSession.NormalizeDisplayPath(displayPath);
            if (normalizedDisplayPath == "/")
            {
                return new DirectoryBrowseResult(
                    true,
                    session.CachedItemsFor("/"),
                    $"正在显示 {session.Host} 的共享根目录。",
                    null
                );
            }

            cancellationToken.ThrowIfCancellationRequested();
            var location = session.ResolveLocation(normalizedDisplayPath);
            if (location is null)
            {
                return new DirectoryBrowseResult(
                    false,
                    [],
                    $"无法解析路径：{normalizedDisplayPath}。",
                    "directory.invalid_path"
                );
            }

            try
            {
                var items = ListDirectoryWithReconnectRetry(session, location);

                cancellationToken.ThrowIfCancellationRequested();
                var mapped = new List<DirectoryItemViewModel>(items.Length);
                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    mapped.Add(new DirectoryItemViewModel(
                        item.Name,
                        BuildDisplayPath(location.ShareName, item.Path),
                        item.Path,
                        location.ShareName,
                        item.IsDir,
                        item.IsDir ? null : item.Size,
                        item.ModifiedTime.HasValue
                            ? DateTimeOffset.FromUnixTimeSeconds(item.ModifiedTime.Value)
                            : null
                    ));
                }

                var ordered = mapped
                    .OrderByDescending(item => item.IsDirectory)
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new DirectoryBrowseResult(
                    true,
                    ordered,
                    $"已从 {location.ShareName}{(location.RemotePath == "/" ? string.Empty : location.RemotePath)} 加载 {ordered.Length} 个项目。",
                    null
                );
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                return new DirectoryBrowseResult(
                    false,
                    [],
                    $"目录加载失败：{ex.Message}",
                    BridgeExceptionClassifier.ErrorCodeFor(ex)
                );
            }
        }, cancellationToken);
    }

    private SmbFileItem[] ListDirectoryWithReconnectRetry(
        WindowsServerSession session,
        DirectoryLocation location
    )
    {
        try
        {
            return ListDirectory(session, location);
        }
        catch (Exception ex) when (IsReconnectableDirectoryFailure(ex) && session.Profile.HasStoredCredential)
        {
            _diagnostics.Info(
                $"目录读取遇到连接中断，准备重连后重试：/{location.ShareName}{(location.RemotePath == "/" ? string.Empty : location.RemotePath)}；错误={ex.Message}"
            );
            _bridge.SmbConnectStoredCredential(
                new SmbConnectStoredCredentialRequest(session.Profile.Id, session.ConnectionId)
            );
            return ListDirectory(session, location);
        }
    }

    private SmbFileItem[] ListDirectory(
        WindowsServerSession session,
        DirectoryLocation location
    )
    {
        return _bridge.SmbListDirectory(
            new SmbListDirectoryRequest(
                location.ShareName,
                location.RemotePath,
                session.ConnectionId
            )
        );
    }

    private static string BuildDisplayPath(string shareName, string remotePath)
    {
        var normalizedRemote = string.IsNullOrWhiteSpace(remotePath) || remotePath == "/"
            ? string.Empty
            : remotePath.Trim();

        if (!normalizedRemote.StartsWith('/') && normalizedRemote.Length > 0)
        {
            normalizedRemote = "/" + normalizedRemote;
        }

        return "/" + shareName + normalizedRemote;
    }

    private static bool IsReconnectableDirectoryFailure(Exception ex)
    {
        if (ex is not RynatCoreBridgeException bridgeException)
        {
            return false;
        }

        if (string.Equals(bridgeException.ErrorCode, "reconnectable", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return bridgeException.Message.Contains("Disconnected", StringComparison.OrdinalIgnoreCase)
            || bridgeException.Message.Contains("未连接", StringComparison.OrdinalIgnoreCase)
            || bridgeException.Message.Contains("连接中断", StringComparison.OrdinalIgnoreCase);
    }
}
