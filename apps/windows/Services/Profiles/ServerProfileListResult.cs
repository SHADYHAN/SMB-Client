using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.Profiles;

public sealed record ServerProfileListResult(
    bool Succeeded,
    IReadOnlyList<ServerProfile> Profiles,
    ServerProfile? ActiveProfile,
    ServerProfile? SavedProfile,
    string Summary,
    string? ErrorCode
);
