using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.Profiles;

public sealed record ServerProfileSaveResult(
    bool Succeeded,
    ServerProfile? Profile,
    string Summary,
    string? ErrorCode
);
