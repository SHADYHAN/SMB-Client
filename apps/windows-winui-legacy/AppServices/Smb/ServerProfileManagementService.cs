using Rynat.Client;
using Rynat.WindowsClient.Infrastructure;

namespace Rynat.WindowsClient.AppServices.Smb;

public sealed class ServerProfileManagementService
{
    private readonly RynatCoreBridge _bridge;
    private readonly WindowsClientDiagnostics _diagnostics;

    public ServerProfileManagementService(
        RynatCoreBridge bridge,
        WindowsClientDiagnostics diagnostics
    )
    {
        _bridge = bridge;
        _diagnostics = diagnostics;
    }

    public Task<ServerProfileManagementResult> SaveProfileAsync(
        ServerProfileDraft draft,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(draft.DisplayName) || string.IsNullOrWhiteSpace(draft.Host))
                {
                    return Failure("服务器名称和地址不能为空。", "server_profile.invalid");
                }

                var profile = _bridge.SaveServerProfile(
                    new SaveServerProfileRequest(
                        string.IsNullOrWhiteSpace(draft.Id) ? null : draft.Id.Trim(),
                        draft.DisplayName.Trim(),
                        draft.Host.Trim(),
                        string.IsNullOrWhiteSpace(draft.Username) ? null : draft.Username.Trim(),
                        draft.AuthMode,
                        draft.DialectPreference,
                        draft.SetActive
                    )
                );

                return new ServerProfileManagementResult(
                    true,
                    $"服务器已保存：{profile.DisplayName}",
                    Profile: profile
                );
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                _diagnostics.Error(ex, $"保存服务器失败：{draft.DisplayName}");
                return Failure(
                    $"保存服务器失败：{ex.Message}",
                    BridgeErrorCode(ex)
                );
            }
        }, cancellationToken);
    }

    public Task<ServerProfileManagementResult> SetActiveProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(profileId))
                {
                    return Failure("请选择要切换的服务器。", "server_profile.invalid_id");
                }

                var snapshot = _bridge.SetActiveServerProfile(new SetActiveServerProfileRequest(profileId.Trim()));
                var activeName = snapshot.ActiveServer?.DisplayName ?? "未知服务器";
                return new ServerProfileManagementResult(
                    true,
                    $"当前服务器已切换为：{activeName}",
                    Snapshot: snapshot,
                    Profile: snapshot.ActiveServer,
                    Credential: snapshot.ActiveCredential
                );
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                _diagnostics.Error(ex, $"切换服务器失败：{profileId}");
                return Failure(
                    $"切换服务器失败：{ex.Message}",
                    BridgeErrorCode(ex)
                );
            }
        }, cancellationToken);
    }

    public Task<ServerProfileManagementResult> DeleteProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(profileId))
                {
                    return Failure("请选择要删除的服务器。", "server_profile.invalid_id");
                }

                var snapshot = _bridge.DeleteServerProfile(new DeleteServerProfileRequest(profileId.Trim()));
                return new ServerProfileManagementResult(
                    true,
                    "服务器已删除。",
                    Snapshot: snapshot,
                    Profile: snapshot.ActiveServer,
                    Credential: snapshot.ActiveCredential
                );
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                _diagnostics.Error(ex, $"删除服务器失败：{profileId}");
                return Failure(
                    $"删除服务器失败：{ex.Message}",
                    BridgeErrorCode(ex)
                );
            }
        }, cancellationToken);
    }

    public Task<ServerProfileManagementResult> SaveCredentialAsync(
        string profileId,
        string username,
        string password,
        bool rememberPassword,
        bool autoLogin,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(profileId)
                    || string.IsNullOrWhiteSpace(username)
                    || string.IsNullOrWhiteSpace(password))
                {
                    return Failure("服务器、账号和密码不能为空。", "credential.invalid");
                }

                // remember_password=false 时不把明文密码经 FFI 发给 Core，
                // 改为删除已存凭据（与 SmbSessionService.ConnectWithCredentialsAsync 一致），
                // 减少不必要的凭据传输。Core 的 save_server_credential 在 false 时也会删除，
                // 此处提前在 bridge 层规避明文密码传输。
                if (!rememberPassword)
                {
                    _bridge.DeleteServerCredential(new DeleteServerCredentialRequest(profileId.Trim()));
                    return new ServerProfileManagementResult(
                        true,
                        "登录凭据已更新，本次不保存密码。",
                        Credential: null
                    );
                }

                var credential = _bridge.SaveServerCredential(
                    new SaveServerCredentialRequest(
                        profileId.Trim(),
                        username.Trim(),
                        password,
                        rememberPassword,
                        autoLogin
                    )
                );

                return new ServerProfileManagementResult(
                    true,
                    "登录凭据已保存。",
                    Credential: credential
                );
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                _diagnostics.Error(ex, $"保存服务器凭据失败：{profileId}");
                return Failure(
                    $"保存服务器凭据失败：{ex.Message}",
                    BridgeErrorCode(ex)
                );
            }
        }, cancellationToken);
    }

    public Task<ServerProfileManagementResult> UpdateCredentialOptionsAsync(
        ServerCredentialOptionsDraft draft,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(draft.ServerProfileId))
                {
                    return Failure("请选择要更新的服务器凭据。", "credential.invalid_id");
                }

                var credential = _bridge.UpdateServerCredentialOptions(
                    new UpdateServerCredentialOptionsRequest(
                        draft.ServerProfileId.Trim(),
                        draft.RememberPassword,
                        draft.AutoLogin
                    )
                );

                return new ServerProfileManagementResult(
                    true,
                    credential is null ? "未找到可更新的凭据。" : "凭据选项已更新。",
                    Credential: credential
                );
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                _diagnostics.Error(ex, $"更新凭据选项失败：{draft.ServerProfileId}");
                return Failure(
                    $"更新凭据选项失败：{ex.Message}",
                    BridgeErrorCode(ex)
                );
            }
        }, cancellationToken);
    }

    public Task<ServerProfileManagementResult> DeleteCredentialAsync(
        string profileId,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(profileId))
                {
                    return Failure("请选择要删除凭据的服务器。", "credential.invalid_id");
                }

                _bridge.DeleteServerCredential(new DeleteServerCredentialRequest(profileId.Trim()));
                return new ServerProfileManagementResult(true, "登录凭据已删除。");
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                _diagnostics.Error(ex, $"删除服务器凭据失败：{profileId}");
                return Failure(
                    $"删除服务器凭据失败：{ex.Message}",
                    BridgeErrorCode(ex)
                );
            }
        }, cancellationToken);
    }

    private static ServerProfileManagementResult Failure(string summary, string errorCode) =>
        new(false, summary, ErrorCode: errorCode);

    private static string BridgeErrorCode(Exception ex) =>
        BridgeExceptionClassifier.ErrorCodeFor(ex, "bridge.error");
}

