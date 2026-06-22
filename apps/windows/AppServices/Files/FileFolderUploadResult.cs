namespace Rynat.WindowsClient.AppServices.Files;

public sealed record FileFolderUploadResult(
    bool Succeeded,
    string Summary,
    int UploadedFiles,
    int CreatedDirectories,
    int ReplacedItems,
    int SkippedItems,
    string? ErrorCode = null,
    int FailedFiles = 0,
    IReadOnlyList<string>? Errors = null
);
