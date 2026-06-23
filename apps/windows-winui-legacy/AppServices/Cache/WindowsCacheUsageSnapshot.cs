namespace Rynat.WindowsClient.AppServices.Cache;

public sealed record WindowsCacheUsageSnapshot(
    long TotalBytes,
    int FileCount,
    int DirectoryCount,
    IReadOnlyList<WindowsCacheBucketUsage> Buckets
);

public sealed record WindowsCacheBucketUsage(
    string Name,
    string Path,
    long Bytes,
    int FileCount,
    int DirectoryCount
);
