using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.Smb;

public interface ISmbSessionService
{
    Task<SmbConnectionFlowResult> ConnectAsync(
        string host,
        string username,
        string password,
        CancellationToken cancellationToken = default
    );

    Task DisconnectAsync(ServerSession session, CancellationToken cancellationToken = default);
}
