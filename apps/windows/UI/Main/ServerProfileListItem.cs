using Rynat.Client;

namespace Rynat.WindowsClient.UI.Main;

public sealed record ServerProfileListItem(
    string Id,
    string DisplayName,
    string Host,
    string CredentialSummary,
    bool HasStoredCredential,
    string? Username
)
{
    public static ServerProfileListItem FromStoredProfile(
        StoredServerProfile profile,
        StoredServerCredential? activeCredential
    )
    {
        var hasStoredCredential = activeCredential?.ServerProfileId == profile.Id;
        var credentialSummary = hasStoredCredential
            ? $"已保存凭据：{activeCredential?.Username}"
            : "未保存凭据";

        return new ServerProfileListItem(
            profile.Id,
            profile.DisplayName,
            profile.Endpoint.Host,
            credentialSummary,
            hasStoredCredential,
            activeCredential?.Username ?? profile.Username
        );
    }
}
