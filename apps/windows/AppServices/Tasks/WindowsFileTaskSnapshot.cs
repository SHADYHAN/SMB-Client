namespace Rynat.WindowsClient.AppServices.Tasks;

public sealed record WindowsFileTaskSnapshot(
    string Id,
    string Kind,
    string Title,
    WindowsFileTaskState State,
    int CompletedItems,
    int? TotalItems,
    string Summary,
    string? ErrorCode,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? CoreOperationId
);
