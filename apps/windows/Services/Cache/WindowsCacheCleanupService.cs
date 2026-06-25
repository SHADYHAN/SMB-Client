using System.IO;

namespace Rynat.WindowsClient.Services.Cache;

public static class WindowsCacheCleanupService
{
    private static readonly TimeSpan PartialFileMaxAge = TimeSpan.FromHours(6);

    public static string AppCacheDirectory(string cacheName, params string?[] buckets)
    {
        var segments = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rynat",
            cacheName
        };
        foreach (var bucket in buckets.Where(bucket => !string.IsNullOrWhiteSpace(bucket)))
        {
            segments.Add(bucket!);
        }

        return Path.Combine(segments.ToArray());
    }

    public static void CleanupDirectory(
        string directory,
        long maxBytes,
        TimeSpan maxAge
    )
    {
        if (maxBytes <= 0 || maxAge <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            if (!System.IO.Directory.Exists(directory))
            {
                return;
            }

            var cutoff = DateTimeOffset.UtcNow.Subtract(maxAge);
            var files = System.IO.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Select(path => FileEntry.TryCreate(path))
                .Where(entry => entry is not null)
                .Cast<FileEntry>()
                .ToList();

            foreach (var file in files.Where(file => ShouldDeleteForAge(file, cutoff)))
            {
                DeleteIfExists(file.Path);
            }

            var remaining = files
                .Where(file => !file.IsPartial && File.Exists(file.Path))
                .OrderByDescending(file => file.LastAccessUtc)
                .ToList();
            long totalBytes = remaining.Sum(file => file.Length);
            foreach (var file in remaining.OrderBy(file => file.LastAccessUtc))
            {
                if (totalBytes <= maxBytes)
                {
                    break;
                }

                if (DeleteIfExists(file.Path))
                {
                    totalBytes -= file.Length;
                }
            }

            DeleteEmptyDirectories(directory);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool ShouldDeleteForAge(FileEntry file, DateTimeOffset cutoff)
    {
        return file.IsPartial
            ? file.LastWriteUtc < DateTimeOffset.UtcNow.Subtract(PartialFileMaxAge)
            : file.LastAccessUtc < cutoff;
    }

    private static bool DeleteIfExists(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void DeleteEmptyDirectories(string rootDirectory)
    {
        foreach (var directory in System.IO.Directory.EnumerateDirectories(rootDirectory, "*", SearchOption.AllDirectories)
            .OrderByDescending(path => path.Length))
        {
            try
            {
                if (!System.IO.Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    System.IO.Directory.Delete(directory);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed record FileEntry(
        string Path,
        long Length,
        DateTimeOffset LastAccessUtc,
        DateTimeOffset LastWriteUtc,
        bool IsPartial
    )
    {
        public static FileEntry? TryCreate(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return new FileEntry(
                    path,
                    info.Length,
                    info.LastAccessTimeUtc,
                    info.LastWriteTimeUtc,
                    path.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                );
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
