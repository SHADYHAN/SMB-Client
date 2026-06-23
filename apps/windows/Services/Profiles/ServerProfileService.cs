using Rynat.Client;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Infrastructure;

namespace Rynat.WindowsClient.Services.Profiles;

public sealed class ServerProfileService : IServerProfileService
{
    private readonly RynatCoreBridge _bridge;

    public ServerProfileService(RynatCoreBridge bridge)
    {
        _bridge = bridge;
    }

    public Task<ServerProfileSaveResult> SaveLoginAsync(
        ServerProfile? existingProfile,
        string host,
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
                var normalizedHost = host.Trim();
                var normalizedUsername = username.Trim();
                if (string.IsNullOrWhiteSpace(normalizedHost) || string.IsNullOrWhiteSpace(normalizedUsername))
                {
                    return Failure("服务器和用户名不能为空。", "server_profile.invalid");
                }

                var profile = _bridge.SaveServerProfile(new SaveServerProfileRequest(
                    string.IsNullOrWhiteSpace(existingProfile?.Id) ? null : existingProfile.Id,
                    DisplayNameFor(existingProfile, normalizedHost),
                    normalizedHost,
                    normalizedUsername,
                    "username_password",
                    "smb3_preferred",
                    true
                ));
                cancellationToken.ThrowIfCancellationRequested();

                StoredServerCredential? credential = null;
                if (rememberPassword)
                {
                    credential = _bridge.SaveServerCredential(new SaveServerCredentialRequest(
                        profile.Id,
                        normalizedUsername,
                        password,
                        true,
                        autoLogin
                    ));
                }
                else
                {
                    try
                    {
                        _bridge.DeleteServerCredential(new DeleteServerCredentialRequest(profile.Id));
                    }
                    catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
                    {
                    }
                }

                return new ServerProfileSaveResult(
                    true,
                    MapProfile(profile, credential),
                    rememberPassword ? "服务器和凭据已保存。" : "服务器已保存，本次不保存密码。",
                    null
                );
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                return Failure("服务器保存失败。", BridgeExceptionClassifier.ErrorCodeFor(ex));
            }
        }, cancellationToken);
    }

    public Task<ServerProfileSaveResult> UpdateCredentialOptionsAsync(
        ServerProfile profile,
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
                if (string.IsNullOrWhiteSpace(profile.Id))
                {
                    return Failure("服务器保存失败。", "server_profile.invalid");
                }

                var credential = _bridge.UpdateServerCredentialOptions(new UpdateServerCredentialOptionsRequest(
                    profile.Id,
                    rememberPassword,
                    autoLogin
                ));
                cancellationToken.ThrowIfCancellationRequested();

                return new ServerProfileSaveResult(
                    true,
                    profile with
                    {
                        HasStoredCredential = credential is not null,
                        AutoLogin = credential?.AutoLogin ?? false,
                        Username = credential?.Username ?? profile.Username
                    },
                    credential is null ? "已关闭记住密码。" : "登录选项已更新。",
                    null
                );
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                return Failure("登录选项保存失败。", BridgeExceptionClassifier.ErrorCodeFor(ex));
            }
        }, cancellationToken);
    }

    private static ServerProfileSaveResult Failure(string summary, string errorCode) =>
        new(false, null, summary, errorCode);

    private static string DisplayNameFor(ServerProfile? existingProfile, string host)
    {
        if (!string.IsNullOrWhiteSpace(existingProfile?.DisplayName))
        {
            return existingProfile.DisplayName;
        }

        return host.Equals("192.168.102.136", StringComparison.OrdinalIgnoreCase)
            ? "共享网盘"
            : host;
    }

    private static ServerProfile MapProfile(StoredServerProfile profile, StoredServerCredential? credential)
    {
        return new ServerProfile(
            profile.Id,
            profile.DisplayName,
            profile.Endpoint.Port is null
                ? profile.Endpoint.Host
                : $"{profile.Endpoint.Host}:{profile.Endpoint.Port}",
            profile.Username ?? credential?.Username,
            credential is not null,
            credential?.AutoLogin ?? false
        );
    }
}
