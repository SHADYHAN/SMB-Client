using Rynat.Client;

namespace Rynat.WindowsClient.AppServices.Smb;

public sealed record ServerProfileManagementResult(
    bool Succeeded,
    string Summary,
    AppBootstrapState? Snapshot = null,
    StoredServerProfile? Profile = null,
    StoredServerCredential? Credential = null,
    string? ErrorCode = null
);
