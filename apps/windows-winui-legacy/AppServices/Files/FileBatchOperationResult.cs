namespace Rynat.WindowsClient.AppServices.Files;

public sealed record FileBatchOperationResult(
    bool Succeeded,
    string Summary,
    int RequestedItems,
    int SucceededItems,
    int FailedItems,
    int SkippedItems,
    int ReplacedItems,
    int CreatedDirectories,
    IReadOnlyList<string> Errors
);
