namespace Rynat.WindowsClient.AppServices.Files;

public sealed record FileDownloadResult(
    bool Succeeded,
    string Summary,
    string? LocalPath,
    int DownloadedFiles = 0,
    int CreatedDirectories = 0,
    int SkippedItems = 0,
    string? ErrorCode = null
);
