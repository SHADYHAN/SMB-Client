using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.LinkActivation;

public interface ILinkActivationService
{
    string? TryExtractStartupLink(IEnumerable<string> rawArguments);

    bool CanOpenWithSession(ServerSession session, LinkOpenRequest request);

    Task<LinkActivationResult> ActivateAsync(
        string rawLink,
        CancellationToken cancellationToken = default
    );
}
