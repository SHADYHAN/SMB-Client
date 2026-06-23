using Rynat.Client;
using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.Directory;

public sealed class DirectoryService : IDirectoryService
{
    private readonly RynatCoreBridge _bridge;

    public DirectoryService(RynatCoreBridge bridge)
    {
        _bridge = bridge;
    }

    public Task<RemoteDirectory> ListAsync(
        ServerSession session,
        string share,
        string path,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = _bridge.SmbListDirectory(new SmbListDirectoryRequest(
                share,
                NormalizeRemotePath(path),
                session.ConnectionId,
                Guid.NewGuid().ToString("N")
            ));

            return new RemoteDirectory(
                share,
                NormalizeRemotePath(path),
                items.Select(item => MapItem(share, item)).ToArray()
            );
        }, cancellationToken);
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
