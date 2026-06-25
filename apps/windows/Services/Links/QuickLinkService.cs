using Rynat.Client;
using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.Links;

public sealed class QuickLinkService : IQuickLinkService
{
    private readonly RynatCoreBridge _bridge;

    public QuickLinkService(RynatCoreBridge bridge)
    {
        _bridge = bridge;
    }

    public Task<QuickLinkInfo> BuildAsync(
        ServerSession session,
        RemoteFileItem item,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var link = _bridge.BuildLink(new BuildLinkRequest(
                session.Host,
                item.Share,
                item.Path,
                item.IsDirectory ? "directory" : "file"
            ));

            return new QuickLinkInfo(link.HttpUrl, link.DeepLinkUrl);
        }, cancellationToken);
    }

    public Task<FavoriteLinkItem> AddFavoriteAsync(
        ServerSession session,
        RemoteFileItem item,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var link = _bridge.GenerateLink(new GenerateLinkRequest(
                session.Host,
                item.Share,
                item.Path,
                item.IsDirectory ? "directory" : "file"
            ));

            return MapFavorite(link);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<FavoriteLinkItem>> ListFavoritesAsync(
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run<IReadOnlyList<FavoriteLinkItem>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _bridge.ListQuickLinks()
                .Select(MapFavorite)
                .ToArray();
        }, cancellationToken);
    }

    public Task DeleteFavoriteAsync(
        string id,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _bridge.DeleteQuickLink(new DeleteQuickLinkRequest(id));
        }, cancellationToken);
    }

    private static FavoriteLinkItem MapFavorite(QuickLink link)
    {
        var name = string.IsNullOrWhiteSpace(link.Target.Name)
            ? DisplayNameFromPath(link.Target.Path, link.Target.Share)
            : link.Target.Name!;
        return new FavoriteLinkItem(
            link.Id,
            name,
            link.Target.ServerHost,
            link.Target.Share,
            NormalizeRemotePath(link.Target.Path),
            IsDirectoryKind(link.Target.Kind),
            link.HttpUrl
        );
    }

    private static bool IsDirectoryKind(string kind) =>
        kind.Equals("directory", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("dir", StringComparison.OrdinalIgnoreCase);

    private static string DisplayNameFromPath(string path, string share)
    {
        var normalized = NormalizeRemotePath(path);
        if (normalized == "/")
        {
            return share;
        }

        return normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? share;
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
