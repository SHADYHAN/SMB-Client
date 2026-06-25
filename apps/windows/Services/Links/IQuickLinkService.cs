using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.Links;

public interface IQuickLinkService
{
    Task<QuickLinkInfo> BuildAsync(
        ServerSession session,
        RemoteFileItem item,
        CancellationToken cancellationToken = default
    );

    Task<FavoriteLinkItem> AddFavoriteAsync(
        ServerSession session,
        RemoteFileItem item,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<FavoriteLinkItem>> ListFavoritesAsync(
        CancellationToken cancellationToken = default
    );

    Task DeleteFavoriteAsync(
        string id,
        CancellationToken cancellationToken = default
    );
}
