using Rynat.Client;
using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.Preview;

public sealed class PreviewService : IPreviewService
{
    private readonly RynatCoreBridge _bridge;

    public PreviewService(RynatCoreBridge bridge)
    {
        _bridge = bridge;
    }

    public Task<PreviewInfo> PlanAsync(
        ServerSession session,
        RemoteFileItem item,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = _bridge.PreviewPlan(new PreviewPlanRequest(
                session.Host,
                item.Share,
                item.Path,
                item.IsDirectory ? "directory" : "file",
                640
            ));

            return new PreviewInfo(
                plan.ContentType,
                plan.Thumbnail?.Url,
                plan.Playback?.Url
            );
        }, cancellationToken);
    }
}
