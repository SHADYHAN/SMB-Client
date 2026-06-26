using Rynat.Client;
using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.Directory;

public sealed class DirectoryService : IDirectoryService
{
    private static readonly TimeSpan DirectoryListTimeout = TimeSpan.FromSeconds(20);
    private readonly RynatCoreBridge _bridge;

    public DirectoryService(RynatCoreBridge bridge)
    {
        _bridge = bridge;
    }

    public async Task<RemoteDirectory> ListAsync(
        ServerSession session,
        string share,
        string path,
        CancellationToken cancellationToken = default
    )
    {
        var listTask = Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = _bridge.SmbListDirectory(new SmbListDirectoryRequest(
                share,
                NormalizeRemotePath(path),
                session.ConnectionId,
                Guid.NewGuid().ToString("N")
            ));
            cancellationToken.ThrowIfCancellationRequested();

            return new RemoteDirectory(
                share,
                NormalizeRemotePath(path),
                items.Select(item => MapItem(share, item)).ToArray()
            );
        }, cancellationToken);

        var completedTask = await Task.WhenAny(
            listTask,
            Task.Delay(DirectoryListTimeout, cancellationToken)
        );
        if (completedTask != listTask)
        {
            throw new TimeoutException("目录加载超时，请重试。");
        }

        return await listTask;
    }

    private static RemoteFileItem MapItem(string share, SmbFileItem item)
    {
        return new RemoteFileItem(
            item.Name,
            share,
            NormalizeRemotePath(item.Path),
            item.IsDir ? RemoteFileKind.Directory : RemoteFileKind.File,
            item.Size,
            item.ModifiedTime is null
                ? null
                : DateTimeOffset.FromUnixTimeSeconds(item.ModifiedTime.Value)
        );
    }

    private static string NormalizeRemotePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        var normalized = path.Replace('\\', '/').Trim();
        return normalized.StartsWith('/') ? normalized : "/" + normalized;
    }
}
