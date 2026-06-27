using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rynat.Client;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Infrastructure;
using Rynat.WindowsClient.Services.FileOperations;
using Rynat.WindowsClient.Services.Cache;
using Rynat.WindowsClient.Services.Smb;

namespace Rynat.WindowsClient.Services.FileTransfers;

public sealed class FileTransferService : IFileTransferService
{
    private const long DragCacheMaxBytes = 4L * 1024 * 1024 * 1024;
    private static readonly TimeSpan DragCacheMaxAge = TimeSpan.FromDays(3);
    private readonly ISmbTaskService _taskService;

    public FileTransferService(ISmbTaskService taskService)
    {
        _taskService = taskService;
    }

    public async Task<DragFilePayloadResult> DownloadFilesAsync(
        ServerSession session,
        IReadOnlyList<RemoteFileItem> items,
        IReadOnlyList<string> localPaths,
        bool replaceExisting,
        IProgress<FileBatchProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        if (items.Count == 0)
        {
            return Failure("请先选择文件。", "download.no_selection");
        }

        if (items.Count != localPaths.Count)
        {
            return Failure("下载失败。", "download.path_mismatch");
        }

        if (items.Any(item => item.IsDirectory))
        {
            return Failure("暂不支持下载文件夹。", "download.directory_not_supported");
        }

        var completed = 0;
        for (var index = 0; index < items.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[index];
            var localPath = localPaths[index];
            if (File.Exists(localPath) && !replaceExisting)
            {
                return Failure($"{Path.GetFileName(localPath)} 已存在。", "download.exists");
            }

            try
            {
                var targetDirectory = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                progress?.Report(new FileBatchProgress(completed, items.Count, item.Name));
                await DownloadFileAsync(session, item, localPath, cancellationToken);
                completed++;
                progress?.Report(new FileBatchProgress(completed, items.Count, item.Name));
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex) || ex is IOException or UnauthorizedAccessException)
            {
                DeleteIfExists(localPath + ".part");
                return completed == 0
                    ? Failure("下载失败。", BridgeExceptionClassifier.ErrorCodeFor(ex, "download.failed"))
                    : Failure($"下载失败，已完成 {completed}/{items.Count} 个文件。", BridgeExceptionClassifier.ErrorCodeFor(ex, "download.failed"));
            }
        }

        return new DragFilePayloadResult(
            true,
            completed == 1 ? "下载完成。" : $"已下载 {completed} 个文件。",
            Array.Empty<DragFilePayload>()
        );
    }

    public async Task<DragFilePayloadResult> CreateDragDownloadPayloadAsync(
        ServerSession session,
        IReadOnlyList<RemoteFileItem> items,
        CancellationToken cancellationToken = default
    )
    {
        if (items.Count == 0)
        {
            return Failure("请先选择文件。", "download.no_selection");
        }

        if (items.Any(item => item.IsDirectory))
        {
            return Failure("暂不支持拖出文件夹。", "download.directory_not_supported");
        }

        var files = new List<DragFilePayload>(items.Count);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var localPath = DragCachePath(session, item);
            if (!IsCompleteLocalFile(localPath, item.Size))
            {
                await DownloadFileAsync(session, item, localPath, cancellationToken);
            }

            files.Add(new DragFilePayload(
                SafeFileName(item.Name),
                item.Size,
                item.ModifiedAt,
                () => File.Open(localPath, FileMode.Open, FileAccess.Read, FileShare.Read),
                localPath
            ));
        }

        return new DragFilePayloadResult(true, "可以拖出。", files);
    }

    private static DragFilePayloadResult Failure(string summary, string errorCode) =>
        new(false, summary, Array.Empty<DragFilePayload>(), errorCode);

    private async Task DownloadFileAsync(
        ServerSession session,
        RemoteFileItem item,
        string localPath,
        CancellationToken cancellationToken
    )
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        var partialPath = localPath + ".part";
        DeleteIfExists(partialPath);

        try
        {
            var payload = JsonSerializer.SerializeToElement(
                new SmbCacheFileRequest(
                    item.Share,
                    item.Path,
                    partialPath,
                    null,
                    session.ConnectionId
                ),
                RynatJsonContext.Default.SmbCacheFileRequest
            );
            var data = await _taskService.RunAsync(
                SmbTaskOperation.CacheFile,
                payload,
                OperationId("drag-download"),
                useIsolatedConnection: false,
                cancellationToken: cancellationToken
            );
            var cached = data?.Deserialize(
                RynatJsonContext.Default.SmbCachedFile
            ) ?? new SmbCachedFile(partialPath, 0);
            cancellationToken.ThrowIfCancellationRequested();
            ReplaceWithCompletedFile(cached.LocalPath, localPath);
        }
        catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex) || ex is IOException or UnauthorizedAccessException)
        {
            DeleteIfExists(partialPath);
            throw;
        }
    }

    private static string DragCachePath(ServerSession session, RemoteFileItem item)
    {
        var cacheDirectory = WindowsCacheCleanupService.AppCacheDirectory(
            "DragCache",
            SafeFileName(session.ConnectionId),
            StableKey(session.Host, item.Share, item.Path, item.Size.ToString(), item.ModifiedAt?.ToUnixTimeSeconds().ToString() ?? "")
        );
        WindowsCacheCleanupService.CleanupDirectory(
            WindowsCacheCleanupService.AppCacheDirectory("DragCache", SafeFileName(session.ConnectionId)),
            DragCacheMaxBytes,
            DragCacheMaxAge
        );

        return Path.Combine(cacheDirectory, SafeFileName(item.Name));
    }

    private static bool IsCompleteLocalFile(string localPath, ulong expectedSize)
    {
        try
        {
            if (!File.Exists(localPath))
            {
                return false;
            }

            var info = new FileInfo(localPath);
            return unchecked((ulong)info.Length) >= expectedSize;
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

    private static void ReplaceWithCompletedFile(string partialPath, string completedPath)
    {
        if (File.Exists(completedPath))
        {
            File.Delete(completedPath);
        }

        File.Move(partialPath, completedPath);
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string OperationId(string prefix) =>
        prefix + "-" + Guid.NewGuid().ToString("N");

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat(value.Select(ch => invalid.Contains(ch) ? '_' : ch));
        return string.IsNullOrWhiteSpace(safe) ? "download" : safe;
    }

    private static string StableKey(params string[] parts)
    {
        var input = string.Join("\n", parts);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }
}
