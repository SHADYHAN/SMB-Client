using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.Smb;

public sealed record SmbConnectionFlowResult(
    bool Succeeded,
    ServerSession? Session,
    string Summary,
    string? ErrorCode
);
