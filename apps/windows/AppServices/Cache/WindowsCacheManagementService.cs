using System.IO;
using Rynat.WindowsClient.Infrastructure;

namespace Rynat.WindowsClient.AppServices.Cache;

public sealed class WindowsCacheManagementService
{
    private readonly WindowsClientDiagnostics _diagnostics;

    public WindowsCacheManagementService(WindowsClientDiagnostics diagnostics)
    {
        _diagnostics = diagnostics;
        CacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rynat"
        );
    }

    public string CacheRoot { get; }

    public string OpenCachePath => Path.Combine(CacheRoot, "OpenCache");

    public string PreviewCachePath => Path.Combine(CacheRoot, "PreviewCache");

    public string DragDownloadCachePath => Path.Combine(CacheRoot, "DragDownloadCache");

    public IReadOnlyList<string> ManagedCachePaths =>
    [
        OpenCachePath,
        PreviewCachePath,
        DragDownloadCachePath
    ];

    public Task<WindowsCacheUsageSnapshot> GetUsageAsync(
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            var buckets = ManagedCachePaths
                .Select(path => CalculateBucketUsage(path, cancellationToken))
                .ToArray();

            return new WindowsCacheUsageSnapshot(
                buckets.Sum(bucket => bucket.Bytes),
                buckets.Sum(bucket => bucket.FileCount),
                buckets.Sum(bucket => bucket.DirectoryCount),
                buckets
            );
        }, cancellationToken);
    }

    public Task<WindowsCacheCleanupResult> ClearAllAsync(
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() => CleanupPaths(ManagedCachePaths, null, cancellationToken), cancellationToken);
    }

    public Task<WindowsCacheCleanupResult> ClearOpenCacheAsync(
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() => CleanupPaths([OpenCachePath], null, cancellationToken), cancellationToken);
    }

    public Task<WindowsCacheCleanupResult> ClearPreviewCacheAsync(
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() => CleanupPaths([PreviewCachePath], null, cancellationToken), cancellationToken);
    }

    public Task<WindowsCacheCleanupResult> ClearDragDownloadCacheAsync(
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() => CleanupPaths([DragDownloadCachePath], null, cancellationToken), cancellationToken);
    }

    public Task<WindowsCacheCleanupResult> CleanupOlderThanAsync(
        TimeSpan maxAge,
        CancellationToken cancellationToken = default
    )
    {
        var cutoff = DateTimeOffset.Now.Subtract(maxAge);
        return Task.Run(() => CleanupPaths(ManagedCachePaths, cutoff, cancellationToken), cancellationToken);
    }

    public async Task<WindowsCacheCleanupResult> CleanupToMaxBytesAsync(
        long maxBytes,
        CancellationToken cancellationToken = default
    )
    {
        if (maxBytes < 0)
        {
            return new WindowsCacheCleanupResult(
                false,
                "缓存上限不能小于 0。",
                0,
                0,
                0,
                0,
                "cache.invalid_limit"
            );
        }

        return await Task.Run(() =>
        {
            var files = EnumerateCacheFiles(cancellationToken)
                .OrderBy(file => file.LastWriteTimeUtc)
                .ToArray();
            var totalBytes = files.Sum(file => SafeLength(file));
            if (totalBytes <= maxBytes)
            {
                return new WindowsCacheCleanupResult(
                    true,
                    $"缓存未超过上限，当前 {FormatBytes(totalBytes)}。",
                    0,
                    0,
                    0,
                    0
                );
            }

            var result = new CleanupStats();
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (totalBytes <= maxBytes)
                {
                    break;
                }

                var length = SafeLength(file);
                if (TryDeleteFile(file.FullName, length, result))
                {
                    totalBytes -= length;
                }
            }

            DeleteEmptyDirectories(ManagedCachePaths, result, cancellationToken);
            return BuildCleanupResult(result, $"缓存已清理到 {FormatBytes(totalBytes)}。");
        }, cancellationToken);
    }

    private WindowsCacheBucketUsage CalculateBucketUsage(
        string path,
        CancellationToken cancellationToken
    )
    {
        var name = Path.GetFileName(path);
        if (!System.IO.Directory.Exists(path))
        {
            return new WindowsCacheBucketUsage(name, path, 0, 0, 0);
        }

        var fileCount = 0;
        var directoryCount = 0;
        long bytes = 0;

        foreach (var directory in EnumerateDirectoriesSafe(path, cancellationToken))
        {
            directoryCount++;
        }

        foreach (var file in EnumerateFilesSafe(path, cancellationToken))
        {
            fileCount++;
            bytes += SafeLength(new FileInfo(file));
        }

        return new WindowsCacheBucketUsage(name, path, bytes, fileCount, directoryCount);
    }

    private WindowsCacheCleanupResult CleanupPaths(
        IEnumerable<string> paths,
        DateTimeOffset? olderThan,
        CancellationToken cancellationToken
    )
    {
        var result = new CleanupStats();

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!System.IO.Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in EnumerateFilesSafe(path, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileInfo = new FileInfo(file);
                if (olderThan.HasValue && fileInfo.LastWriteTimeUtc > olderThan.Value.UtcDateTime)
                {
                    continue;
                }

                TryDeleteFile(file, SafeLength(fileInfo), result);
            }
        }

        DeleteEmptyDirectories(paths, result, cancellationToken);

        var summary = olderThan.HasValue
            ? $"已清理 {olderThan.Value:yyyy-MM-dd HH:mm} 之前的缓存。"
            : "缓存已清理。";
        return BuildCleanupResult(result, summary);
    }

    private IEnumerable<FileInfo> EnumerateCacheFiles(CancellationToken cancellationToken)
    {
        foreach (var path in ManagedCachePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!System.IO.Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in EnumerateFilesSafe(path, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsActivePartialFile(file))
                {
                    continue;
                }

                yield return new FileInfo(file);
            }
        }
    }

    private void DeleteEmptyDirectories(
        IEnumerable<string> paths,
        CleanupStats result,
        CancellationToken cancellationToken
    )
    {
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!System.IO.Directory.Exists(path))
            {
                continue;
            }

            var directories = EnumerateDirectoriesSafe(path, cancellationToken)
                .OrderByDescending(directory => directory.Length)
                .ToArray();

            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!System.IO.Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        System.IO.Directory.Delete(directory);
                        result.DeletedDirectories++;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    result.SkippedItems++;
                    _diagnostics.Error(ex, $"删除空缓存目录失败：{directory}");
                }
            }
        }
    }

    private bool TryDeleteFile(string path, long bytes, CleanupStats result)
    {
        try
        {
            if (IsActivePartialFile(path))
            {
                result.SkippedItems++;
                return false;
            }

            File.Delete(path);
            result.DeletedFiles++;
            result.FreedBytes += bytes;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.SkippedItems++;
            _diagnostics.Error(ex, $"删除缓存文件失败：{path}");
            return false;
        }
    }

    private IEnumerable<string> EnumerateFilesSafe(
        string path,
        CancellationToken cancellationToken
    )
    {
        foreach (var directory in EnumerateDirectoryTreeSafe(path, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] files;
            try
            {
                files = System.IO.Directory.GetFiles(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _diagnostics.Error(ex, $"枚举缓存文件失败：{directory}");
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return file;
            }
        }
    }

    private IEnumerable<string> EnumerateDirectoriesSafe(
        string path,
        CancellationToken cancellationToken
    )
    {
        foreach (var directory in EnumerateDirectoryTreeSafe(path, cancellationToken).Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return directory;
        }
    }

    private IEnumerable<string> EnumerateDirectoryTreeSafe(
        string root,
        CancellationToken cancellationToken
    )
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            yield return current;

            string[] children;
            try
            {
                children = System.IO.Directory.GetDirectories(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _diagnostics.Error(ex, $"枚举缓存目录失败：{current}");
                continue;
            }

            foreach (var child in children)
            {
                pending.Push(child);
            }
        }
    }

    private static WindowsCacheCleanupResult BuildCleanupResult(
        CleanupStats result,
        string prefix
    )
    {
        var summary = $"{prefix} 删除文件 {result.DeletedFiles} 个，释放 {FormatBytes(result.FreedBytes)}。";
        if (result.DeletedDirectories > 0)
        {
            summary += $" 删除空目录 {result.DeletedDirectories} 个。";
        }
        if (result.SkippedItems > 0)
        {
            summary += $" 跳过 {result.SkippedItems} 项。";
        }

        return new WindowsCacheCleanupResult(
            true,
            summary,
            result.FreedBytes,
            result.DeletedFiles,
            result.DeletedDirectories,
            result.SkippedItems
        );
    }

    private static long SafeLength(FileInfo file)
    {
        try
        {
            return file.Exists ? file.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.#} {units[index]}";
    }

    private static bool IsActivePartialFile(string path) =>
        string.Equals(Path.GetExtension(path), ".part", StringComparison.OrdinalIgnoreCase);

    private sealed class CleanupStats
    {
        public long FreedBytes { get; set; }

        public int DeletedFiles { get; set; }

        public int DeletedDirectories { get; set; }

        public int SkippedItems { get; set; }
    }
}
