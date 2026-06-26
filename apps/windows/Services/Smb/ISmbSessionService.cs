using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.Smb;

public interface ISmbSessionService
{
    Task<SmbConnectionFlowResult> ConnectAsync(
        string host,
        string username,
        string password,
        string? connectionId = null,
        CancellationToken cancellationToken = default
    );

    Task<SmbConnectionFlowResult> ConnectStoredCredentialAsync(
        ServerProfile profile,
        CancellationToken cancellationToken = default
    );

    Task DisconnectAsync(ServerSession session, CancellationToken cancellationToken = default);

    Task<bool> IsConnectedAsync(ServerSession session, CancellationToken cancellationToken = default);
}
