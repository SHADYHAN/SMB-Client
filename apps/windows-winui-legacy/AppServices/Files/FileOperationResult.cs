namespace Rynat.WindowsClient.AppServices.Files;

public sealed record FileOperationResult(
    bool Succeeded,
    string Summary,
    string? ErrorCode = null,
    int SucceededItems = 0,
    int SkippedItems = 0,
    int ReplacedItems = 0,
    int FailedItems = 0,
    IReadOnlyList<string>? Errors = null
);
