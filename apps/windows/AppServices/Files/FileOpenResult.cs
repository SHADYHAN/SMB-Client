namespace Rynat.WindowsClient.AppServices.Files;

public sealed record FileOpenResult(
    bool Succeeded,
    string Summary,
    string? LocalPath,
    string? ErrorCode
);
