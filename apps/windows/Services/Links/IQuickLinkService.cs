using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.Links;

public interface IQuickLinkService
{
    Task<QuickLinkInfo> BuildAsync(
        ServerSession session,
        RemoteFileItem item,
        CancellationToken cancellationToken = default
    );
}
