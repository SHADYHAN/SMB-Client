namespace Rynat.WindowsClient.AppServices.Cache;

public sealed record WindowsCacheCleanupResult(
    bool Succeeded,
    string Summary,
    long FreedBytes,
    int DeletedFiles,
    int DeletedDirectories,
    int SkippedItems,
    string? ErrorCode = null
);
