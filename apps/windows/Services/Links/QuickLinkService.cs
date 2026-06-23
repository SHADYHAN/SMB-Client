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
}
