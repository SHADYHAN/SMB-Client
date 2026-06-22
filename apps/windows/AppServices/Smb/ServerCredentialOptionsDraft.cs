namespace Rynat.WindowsClient.AppServices.Smb;

public sealed record ServerCredentialOptionsDraft(
    string ServerProfileId,
    bool RememberPassword,
    bool AutoLogin
);
