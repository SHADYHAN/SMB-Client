using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.Preview;

public interface IPreviewService
{
    Task<PreviewInfo> PlanAsync(
        ServerSession session,
        RemoteFileItem item,
        CancellationToken cancellationToken = default
    );
}
