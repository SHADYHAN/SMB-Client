using System.IO;
using Rynat.WindowsClient.AppServices.Tasks;
using Rynat.WindowsClient.Infrastructure;
using Rynat.WindowsClient.UI.Main;

namespace Rynat.WindowsClient.AppServices.Files;

public sealed class FileBatchOperationService
{
    private readonly FileDownloadService _downloadService;
    private readonly FileWriteService _writeService;
    private readonly FileFolderUploadService _folderUploadService;
    private readonly WindowsClientDiagnostics _diagnostics;

    public FileBatchOperationService(
        FileDownloadService downloadService,
        FileWriteService writeService,
        FileFolderUploadService folderUploadService,
        WindowsClientDiagnostics diagnostics
    )
    {
        _downloadService = downloadService;
        _writeService = writeService;
        _folderUploadService = folderUploadService;
        _diagnostics = diagnostics;
    }

    public async Task<FileBatchOperationResult> DownloadItemsAsync(
        WindowsServerSession session,
        IReadOnlyList<DirectoryItemViewModel> items,
        string localParentDirectory,
        WindowsFileTaskHandle? task = null,
        CancellationToken cancellationToken = default
    )
    {
        var stats = new BatchStats(items.Count);
        try
        {
            task?.Start($"正在下载 {items.Count} 个项目...");
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                task?.CancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var result = item.IsDirectory
                        ? await _downloadService.DownloadDirectoryAsync(
                            session,
                            item,
                            localParentDirectory,
                            task,
                            cancellationToken,
                            updateTaskState: false
                        )
                        : await _downloadService.DownloadAsync(
                            session,
                            item,
                            Path.Combine(localParentDirectory, SafeFileName(item.Name)),
                            task,
                            cancellationToken,
                            updateTaskState: false
                        );
                    ApplyDownloadResult(stats, result);
                }
                catch (Exception ex)
                {
                    stats.FailedItems++;
                    stats.Errors.Add($"{item.Name}：{ex.Message}");
                    _diagnostics.Error(ex, $"批量下载失败：{item.DisplayPath}");
                }

                task?.ReportProgress(stats.ProcessedItems, items.Count, $"批量下载中：已处理 {stats.ProcessedItems}/{items.Count} 项。");
            }

            return Finish(task, stats, "批量下载");
        }
        catch (OperationCanceledException)
        {
            task?.Cancel("已取消批量下载。");
            return BuildCancelledResult(stats, "批量下载");
        }
        catch (Exception ex)
        {
            return FailUnexpected(task, stats, "批量下载", ex);
        }
    }

    public async Task<FileBatchOperationResult> DeleteItemsAsync(
        WindowsServerSession session,
        IReadOnlyList<DirectoryItemViewModel> items,
        WindowsFileTaskHandle? task = null,
        CancellationToken cancellationToken = default
    )
    {
        var stats = new BatchStats(items.Count);
        try
        {
            task?.Start($"正在删除 {items.Count} 个项目...");
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                task?.CancellationToken.ThrowIfCancellationRequested();
                var result = await _writeService.DeleteAsync(session, item, cancellationToken);
                ApplyOperationResult(stats, item.Name, result);
                task?.ReportProgress(stats.ProcessedItems, items.Count, $"批量删除中：已处理 {stats.ProcessedItems}/{items.Count} 项。");
            }

            return Finish(task, stats, "批量删除");
        }
        catch (OperationCanceledException)
        {
            task?.Cancel("已取消批量删除。");
            return BuildCancelledResult(stats, "批量删除");
        }
        catch (Exception ex)
        {
            return FailUnexpected(task, stats, "批量删除", ex);
        }
    }

    public async Task<FileBatchOperationResult> UploadLocalPathsAsync(
        WindowsServerSession session,
        string currentDisplayPath,
        IReadOnlyList<string> localPaths,
        IReadOnlyDictionary<string, UploadConflictDecision>? conflictDecisions = null,
        WindowsFileTaskHandle? task = null,
        CancellationToken cancellationToken = default
    )
    {
        var stats = new BatchStats(localPaths.Count);
        try
        {
            var filePaths = localPaths.Where(File.Exists).ToArray();
            var directoryPaths = localPaths.Where(System.IO.Directory.Exists).ToArray();
            stats.SkippedItems += Math.Max(0, localPaths.Count - filePaths.Length - directoryPaths.Length);

            task?.Start($"正在上传 {localPaths.Count} 个本地项目...");
            if (filePaths.Length > 0)
            {
                var fileResult = await _writeService.UploadFilesAsync(
                    session,
                    currentDisplayPath,
                    filePaths,
                    conflictDecisions,
                    task,
                    cancellationToken
                );

                if (fileResult.ErrorCode == "upload.cancelled")
                {
                    throw new OperationCanceledException(fileResult.Summary);
                }

                stats.SucceededItems += fileResult.SucceededItems;
                stats.SkippedItems += fileResult.SkippedItems;
                stats.ReplacedItems += fileResult.ReplacedItems;
                stats.FailedItems += fileResult.FailedItems;
                foreach (var error in fileResult.Errors ?? [])
                {
                    stats.Errors.Add(error);
                }

                task?.ReportProgress(stats.ProcessedItems, localPaths.Count, $"批量上传中：已处理 {stats.ProcessedItems}/{localPaths.Count} 项。");
            }

            foreach (var directoryPath in directoryPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                task?.CancellationToken.ThrowIfCancellationRequested();
                var result = await _folderUploadService.UploadDirectoryAsync(
                    session,
                    currentDisplayPath,
                    directoryPath,
                    conflictDecisions,
                    task,
                    cancellationToken,
                    updateTaskState: true,
                    manageTaskState: false
                );

                if (result.ErrorCode == "upload.cancelled")
                {
                    throw new OperationCanceledException(result.Summary);
                }

                stats.SucceededItems += result.UploadedFiles;
                stats.ReplacedItems += result.ReplacedItems;
                stats.CreatedDirectories += result.CreatedDirectories;
                stats.SkippedItems += result.SkippedItems;
                stats.FailedItems += result.FailedFiles;
                foreach (var error in result.Errors ?? [])
                {
                    stats.Errors.Add(error);
                }

                if (result.ErrorCode == "upload.skipped" && result.SkippedItems == 0)
                {
                    stats.SkippedItems++;
                }

                task?.ReportProgress(stats.ProcessedItems, localPaths.Count, $"批量上传中：已处理 {stats.ProcessedItems}/{localPaths.Count} 项。");
            }

            return Finish(task, stats, "批量上传");
        }
        catch (OperationCanceledException)
        {
            task?.Cancel("已取消批量上传。");
            return BuildCancelledResult(stats, "批量上传");
        }
        catch (Exception ex)
        {
            return FailUnexpected(task, stats, "批量上传", ex);
        }
    }

    public async Task<FileBatchOperationResult> PasteClipboardAsync(
        WindowsServerSession session,
        string currentDisplayPath,
        FileClipboardState clipboard,
        IReadOnlyDictionary<string, UploadConflictDecision>? conflictDecisions = null,
        WindowsFileTaskHandle? task = null,
        CancellationToken cancellationToken = default
    )
    {
        var stats = new BatchStats(clipboard.Entries.Count);
        var label = clipboard.Mode == FileClipboardMode.Copy ? "批量复制" : "批量移动";
        try
        {
            task?.Start(clipboard.Mode == FileClipboardMode.Copy ? "正在批量复制..." : "正在批量移动...");
            var result = await _writeService.PasteAsync(
                session,
                currentDisplayPath,
                clipboard,
                conflictDecisions,
                cancellationToken
            );

            if (result.Succeeded)
            {
                stats.SucceededItems = result.SucceededItems > 0
                    ? result.SucceededItems
                    : clipboard.Entries.Count;
                stats.SkippedItems += result.SkippedItems;
                stats.ReplacedItems += result.ReplacedItems;
            }
            else if (result.ErrorCode == "paste.cancelled")
            {
                throw new OperationCanceledException(result.Summary);
            }
            else
            {
                stats.FailedItems = clipboard.Entries.Count;
                stats.Errors.Add(result.Summary);
            }

            return Finish(task, stats, label);
        }
        catch (OperationCanceledException)
        {
            task?.Cancel($"已取消{label}。");
            return BuildCancelledResult(stats, label);
        }
        catch (Exception ex)
        {
            return FailUnexpected(task, stats, label, ex);
        }
    }

    private FileBatchOperationResult FailUnexpected(
        WindowsFileTaskHandle? task,
        BatchStats stats,
        string label,
        Exception exception
    )
    {
        _diagnostics.Error(exception, $"{label}异常结束");
        stats.FailedItems += Math.Max(1, stats.RequestedItems - stats.ProcessedItems);
        stats.Errors.Add(exception.Message);
        var result = BuildResult(stats, label);
        task?.Fail(result.Summary, "batch.failed");
        return result;
    }

    private static FileBatchOperationResult Finish(
        WindowsFileTaskHandle? task,
        BatchStats stats,
        string label
    )
    {
        var result = BuildResult(stats, label);
        if (result.Succeeded)
        {
            task?.Complete(result.Summary);
        }
        else
        {
            task?.Fail(result.Summary, "batch.failed");
        }

        return result;
    }

    private static FileBatchOperationResult BuildCancelledResult(BatchStats stats, string label) =>
        new(
            false,
            $"{label}已取消：已完成 {stats.SucceededItems} 项，跳过 {stats.SkippedItems} 项，失败 {stats.FailedItems} 项。",
            stats.RequestedItems,
            stats.SucceededItems,
            stats.FailedItems,
            stats.SkippedItems,
            stats.ReplacedItems,
            stats.CreatedDirectories,
            stats.Errors
        );

    private static void ApplyDownloadResult(BatchStats stats, FileDownloadResult result)
    {
        if (result.Succeeded)
        {
            stats.SucceededItems++;
            stats.CreatedDirectories += result.CreatedDirectories;
            stats.SkippedItems += result.SkippedItems;
            return;
        }

        if (result.ErrorCode?.Contains("cancelled", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new OperationCanceledException(result.Summary);
        }

        if (result.ErrorCode?.Contains("skipped", StringComparison.OrdinalIgnoreCase) == true)
        {
            stats.SkippedItems++;
            return;
        }

        stats.FailedItems++;
        stats.Errors.Add(result.Summary);
    }

    private static void ApplyOperationResult(
        BatchStats stats,
        string itemName,
        FileOperationResult result
    )
    {
        if (result.Succeeded)
        {
            stats.SucceededItems++;
            return;
        }

        if (result.ErrorCode?.Contains("cancelled", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new OperationCanceledException(result.Summary);
        }

        if (result.ErrorCode?.Contains("skipped", StringComparison.OrdinalIgnoreCase) == true)
        {
            stats.SkippedItems++;
            return;
        }

        stats.FailedItems++;
        stats.Errors.Add($"{itemName}：{result.Summary}");
    }

    private static FileBatchOperationResult BuildResult(BatchStats stats, string label)
    {
        var succeeded = stats.FailedItems == 0 && stats.SucceededItems > 0;
        var parts = new List<string> { $"{label}完成：成功 {stats.SucceededItems} 项" };
        if (stats.SkippedItems > 0)
        {
            parts.Add($"跳过 {stats.SkippedItems} 项");
        }
        if (stats.FailedItems > 0)
        {
            parts.Add($"失败 {stats.FailedItems} 项");
        }
        if (stats.ReplacedItems > 0)
        {
            parts.Add($"覆盖 {stats.ReplacedItems} 项");
        }
        if (stats.CreatedDirectories > 0)
        {
            parts.Add($"新建文件夹 {stats.CreatedDirectories} 个");
        }

        return new FileBatchOperationResult(
            succeeded,
            string.Join("，", parts) + "。",
            stats.RequestedItems,
            stats.SucceededItems,
            stats.FailedItems,
            stats.SkippedItems,
            stats.ReplacedItems,
            stats.CreatedDirectories,
            stats.Errors
        );
    }

    private static string SafeFileName(string name)
    {
        var safeName = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        return string.IsNullOrWhiteSpace(safeName) ? "download" : safeName;
    }

    private sealed class BatchStats
    {
        public BatchStats(int requestedItems)
        {
            RequestedItems = requestedItems;
        }

        public int RequestedItems { get; }

        public int SucceededItems { get; set; }

        public int FailedItems { get; set; }

        public int SkippedItems { get; set; }

        public int ReplacedItems { get; set; }

        public int CreatedDirectories { get; set; }

        public int ProcessedItems => SucceededItems + FailedItems + SkippedItems;

        public List<string> Errors { get; } = [];
    }
}
