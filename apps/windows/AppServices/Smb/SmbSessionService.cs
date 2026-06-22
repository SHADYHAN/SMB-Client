using Rynat.Client;
using Rynat.WindowsClient.UI.Main;

namespace Rynat.WindowsClient.AppServices.Smb;

public sealed class SmbSessionService
{
    private readonly RynatCoreBridge _bridge;

    public SmbSessionService(RynatCoreBridge bridge)
    {
        _bridge = bridge;
    }

    public Task<SmbConnectFlowResult> ConnectStoredProfileAsync(
        ServerProfileListItem profile,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? connectionId = profile.Id;
            try
            {
                var result = _bridge.SmbConnectStoredCredential(
                    new SmbConnectStoredCredentialRequest(profile.Id, profile.Id)
                );
                cancellationToken.ThrowIfCancellationRequested();
                connectionId = result.ConnectionId;

                var session = new WindowsServerSession(
                    result.ConnectionId,
                    result.Host,
                    result.DialectLabel,
                    profile,
                    result.Shares
                        .Select(share => new ShareListItem(share.Name, share.Comment))
                        .ToArray()
                );

                session.NavigateTo("/");
                session.CacheDirectory(
                    "/",
                    session.Shares
                        .Select(share => new DirectoryItemViewModel(
                            share.Name,
                            "/" + share.Name,
                            "/",
                            share.Name,
                            true,
                            null,
                            null
                        ))
                        .ToArray()
                );

                return new SmbConnectFlowResult(
                    true,
                    session,
                    LoadBootstrap(cancellationToken),
                    $"已连接到 {result.Host}，协议：{result.DialectLabel}，共发现 {result.Shares.Length} 个共享。",
                    null
                );
            }
            catch (Exception ex) when (IsBridgeFailure(ex))
            {
                DisconnectQuietly(connectionId);
                return new SmbConnectFlowResult(
                    false,
                    null,
                    null,
                    $"使用已保存凭据连接失败：{ex.Message}",
                    ErrorCodeFor(ex)
                );
            }
            catch (OperationCanceledException)
            {
                DisconnectQuietly(connectionId);
                return new SmbConnectFlowResult(
                    false,
                    null,
                    null,
                    "已取消连接服务器。",
                    "connect.cancelled"
                );
            }
        }, cancellationToken);
    }

    public Task<SmbConnectFlowResult> ConnectWithCredentialsAsync(
        ServerProfileListItem profile,
        string username,
        string password,
        bool rememberPassword,
        bool autoLogin,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? connectionId = null;
            try
            {
                connectionId = string.IsNullOrWhiteSpace(profile.Id)
                    ? Guid.NewGuid().ToString()
                    : profile.Id;

                var result = _bridge.SmbConnect(
                    new SmbConnectRequest(profile.Host, username, password, connectionId)
                );
                cancellationToken.ThrowIfCancellationRequested();

                var savedProfile = _bridge.SaveServerProfile(
                    new SaveServerProfileRequest(
                        profile.Id,
                        profile.DisplayName,
                        result.Host,
                        username,
                        "username_password",
                        "smb3_preferred",
                        true
                    )
                );
                cancellationToken.ThrowIfCancellationRequested();

                StoredServerCredential? savedCredential = null;
                if (rememberPassword)
                {
                    savedCredential = _bridge.SaveServerCredential(
                        new SaveServerCredentialRequest(
                            savedProfile.Id,
                            username,
                            password,
                            true,
                            autoLogin
                        )
                    );
                }
                else
                {
                    try
                    {
                        _bridge.DeleteServerCredential(
                            new DeleteServerCredentialRequest(savedProfile.Id)
                        );
                    }
                    catch (RynatCoreBridgeException)
                    {
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();

                var session = new WindowsServerSession(
                    result.ConnectionId,
                    result.Host,
                    result.DialectLabel,
                    ServerProfileListItem.FromStoredProfile(
                        savedProfile,
                        savedCredential
                    ),
                    result.Shares
                        .Select(share => new ShareListItem(share.Name, share.Comment))
                        .ToArray()
                );

                session.NavigateTo("/");
                session.CacheDirectory(
                    "/",
                    session.Shares
                        .Select(share => new DirectoryItemViewModel(
                            share.Name,
                            "/" + share.Name,
                            "/",
                            share.Name,
                            true,
                            null,
                            null
                        ))
                        .ToArray()
                );

                return new SmbConnectFlowResult(
                    true,
                    session,
                    LoadBootstrap(cancellationToken),
                    $"已连接到 {result.Host}，协议：{result.DialectLabel}，共发现 {result.Shares.Length} 个共享。",
                    null
                );
            }
            catch (Exception ex) when (IsBridgeFailure(ex))
            {
                DisconnectQuietly(connectionId);
                return new SmbConnectFlowResult(
                    false,
                    null,
                    null,
                    $"使用账号密码连接失败：{ex.Message}",
                    ErrorCodeFor(ex)
                );
            }
            catch (OperationCanceledException)
            {
                DisconnectQuietly(connectionId);
                return new SmbConnectFlowResult(
                    false,
                    null,
                    null,
                    "已取消连接服务器。",
                    "connect.cancelled"
                );
            }
        }, cancellationToken);
    }

    public void Disconnect(WindowsServerSession? session)
    {
        if (session is null)
        {
            return;
        }

        DisconnectQuietly(session.ConnectionId);
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
        catch (Exception ex) when (IsBridgeFailure(ex))
        {
        }
    }

    private AppBootstrapState LoadBootstrap(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = _bridge.AppBootstrap();
        cancellationToken.ThrowIfCancellationRequested();
        return snapshot;
    }

    private static bool IsBridgeFailure(Exception ex) =>
        ex is RynatCoreBridgeException
            or DllNotFoundException
            or EntryPointNotFoundException
            or System.Runtime.InteropServices.SEHException
            or System.Text.Json.JsonException;

    private static string ErrorCodeFor(Exception ex) =>
        ex is RynatCoreBridgeException bridgeEx
            ? bridgeEx.ErrorCode ?? "bridge.failed"
            : "bridge.failed";
}
