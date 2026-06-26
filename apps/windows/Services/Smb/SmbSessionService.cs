using Rynat.Client;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Infrastructure;

namespace Rynat.WindowsClient.Services.Smb;

public sealed class SmbSessionService : ISmbSessionService
{
    private readonly RynatCoreBridge _bridge;

    public SmbSessionService(RynatCoreBridge bridge)
    {
        _bridge = bridge;
    }

    public Task<SmbConnectionFlowResult> ConnectAsync(
        string host,
        string username,
        string password,
        string? connectionId = null,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scopedConnectionId = string.IsNullOrWhiteSpace(connectionId)
                ? Guid.NewGuid().ToString("N")
                : connectionId.Trim();

            try
            {
                var result = _bridge.SmbConnect(new SmbConnectRequest(
                    host.Trim(),
                    username.Trim(),
                    password,
                    scopedConnectionId
                ));
                cancellationToken.ThrowIfCancellationRequested();

                return Success(result);
            }
            catch (OperationCanceledException)
            {
                DisconnectQuietly(scopedConnectionId);
                return Cancelled();
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                DisconnectQuietly(scopedConnectionId);
                return Failure(ex);
            }
        }, cancellationToken);
    }

    public Task<SmbConnectionFlowResult> ConnectStoredCredentialAsync(
        ServerProfile profile,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connectionId = profile.Id;

            try
            {
                var result = _bridge.SmbConnectStoredCredential(new SmbConnectStoredCredentialRequest(
                    profile.Id,
                    profile.Id
                ));
                cancellationToken.ThrowIfCancellationRequested();

                return Success(result);
            }
            catch (OperationCanceledException)
            {
                DisconnectQuietly(connectionId);
                return Cancelled();
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                DisconnectQuietly(connectionId);
                return Failure(ex);
            }
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

    public Task<bool> IsConnectedAsync(ServerSession session, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return _bridge.SmbDiagnostics(new SmbConnectionScopedRequest(session.ConnectionId)).Connected;
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                return false;
            }
        }, cancellationToken);
    }

    private static SmbConnectionFlowResult Success(SmbConnectResult result)
    {
        var session = new ServerSession(
            result.ConnectionId,
            result.Host,
            result.DialectLabel,
            result.Shares.Select(share => new ServerShare(share.Name, share.Comment)).ToArray()
        );

        return new SmbConnectionFlowResult(
            true,
            session,
            $"已连接 {result.Host}，发现 {result.Shares.Length} 个共享。",
            null
        );
    }

    private static SmbConnectionFlowResult Cancelled() =>
        new(false, null, "已取消连接。", "connect.cancelled");

    private static SmbConnectionFlowResult Failure(Exception ex)
    {
        var errorCode = BridgeExceptionClassifier.ErrorCodeFor(ex);
        return new SmbConnectionFlowResult(
            false,
            null,
            UserFacingConnectError(errorCode),
            errorCode
        );
    }

    private void DisconnectQuietly(string? connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            return;
        }

        try
        {
            _bridge.SmbDisconnect(new SmbConnectionScopedRequest(connectionId));
        }
        catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
        {
        }
    }

    private static string UserFacingConnectError(string errorCode)
    {
        if (errorCode.Equals("auth", StringComparison.OrdinalIgnoreCase)
            || errorCode.EndsWith(".auth", StringComparison.OrdinalIgnoreCase))
        {
            return "账号或密码错误。";
        }

        if (errorCode.Equals("not_found", StringComparison.OrdinalIgnoreCase)
            || errorCode.EndsWith(".not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "找不到服务器。";
        }

        if (errorCode.Equals("permission", StringComparison.OrdinalIgnoreCase)
            || errorCode.EndsWith(".permission", StringComparison.OrdinalIgnoreCase))
        {
            return "没有权限访问。";
        }

        return "连接失败。";
    }
}
