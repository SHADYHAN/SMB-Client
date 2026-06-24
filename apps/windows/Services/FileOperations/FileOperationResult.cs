namespace Rynat.WindowsClient.Services.FileOperations;

public sealed record FileOperationResult(
    bool Succeeded,
    string Summary,
    string? ErrorCode = null
);
