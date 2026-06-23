namespace Rynat.WindowsClient.Domain;

public sealed record ServerProfile(
    string Id,
    string DisplayName,
    string Host,
    string? Username
);
