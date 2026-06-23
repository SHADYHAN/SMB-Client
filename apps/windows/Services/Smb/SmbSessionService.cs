using Rynat.Client;
using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.Smb;

public sealed class SmbSessionService : ISmbSessionService
{
    private readonly RynatCoreBridge _bridge;

    public SmbSessionService(RynatCoreBridge bridge)
    {
        _bridge = bridge;
    }

    public Task<ServerSession> ConnectAsync(
        string host,
        string username,
        string password,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _bridge.SmbConnect(new SmbConnectRequest(
                host.Trim(),
                username.Trim(),
                password,
                Guid.NewGuid().ToString("N")
            ));

            return new ServerSession(
                result.ConnectionId,
                result.Host,
                result.DialectLabel,
                result.Shares.Select(share => new ServerShare(share.Name, share.Comment)).ToArray()
            );
        }, cancellationToken);
    }

    public Task DisconnectAsync(ServerSession session, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _bridge.SmbDisconnect(new SmbConnectionScopedRequest(session.ConnectionId));
        }, cancellationToken);
    }
}
