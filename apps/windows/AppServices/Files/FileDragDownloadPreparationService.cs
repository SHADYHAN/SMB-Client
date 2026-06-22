using Rynat.WindowsClient.AppServices.Tasks;
using Rynat.WindowsClient.Infrastructure;
using Rynat.WindowsClient.UI.Main;

namespace Rynat.WindowsClient.AppServices.Files;

public sealed class FileDragDownloadPreparationService
{
    private const ulong AutoPrepareMaxBytes = 64UL * 1024 * 1024;
    private readonly FileDownloadService _downloadService;
    private readonly WindowsFileTaskService _taskService;
    private readonly WindowsClientDiagnostics _diagnostics;
    private readonly Dictionary<string, FileDownloadResult> _prepared = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _activePreparationCts;
    private WindowsFileTaskHandle? _activeTask;

    public FileDragDownloadPreparationService(
        FileDownloadService downloadService,
        WindowsFileTaskService taskService,
        WindowsClientDiagnostics diagnostics
    )
    {
        _downloadService = downloadService;
        _taskService = taskService;
        _diagnostics = diagnostics;
    }

    public FileDownloadResult? TryGetPrepared(DirectoryItemViewModel item)
    {
        lock (_syncRoot)
        {
            return _prepared.TryGetValue(item.DisplayPath, out var result)
                ? result
                : null;
        }
    }

    public async Task<FileDownloadResult> PrepareAsync(
        WindowsServerSession session,
        DirectoryItemViewModel item,
        CancellationToken cancellationToken = default,
        bool automatic = false
    )
    {
        if (automatic && !ShouldAutoPrepare(item))
        {
            CancelActive();
            return new FileDownloadResult(
                false,
                item.IsDirectory
                    ? $"已选择文件夹：{item.Name}。拖出时再准备下载。"
                    : $"文件较大或大小未知：{item.Name}。拖出时再准备下载。",
                null,
                ErrorCode: "download.auto_prepare_skipped"
            );
        }

        if (item.IsDirectory)
        {
            return await PrepareDirectoryAsync(session, item, cancellationToken);
        }

        var existing = TryGetPrepared(item);
        if (existing?.Succeeded == true && !string.IsNullOrWhiteSpace(existing.LocalPath))
        {
            return existing;
        }

        var linkedCts = ReplaceActivePreparation(cancellationToken);
        var task = _taskService.Start("drag_download_prepare", $"准备拖出：{item.Name}", 1);
        RegisterActiveTask(task);
        try
        {
            var result = await _downloadService.PrepareDragDownloadAsync(
                session,
                item,
                task,
                linkedCts.Token
            );
            RememberPrepared(item, result);
            return result;
        }
        finally
        {
            FinishActivePreparation(linkedCts, task);
        }
    }

    public async Task<FileDownloadResult> PrepareDirectoryAsync(
        WindowsServerSession session,
        DirectoryItemViewModel item,
        CancellationToken cancellationToken = default
    )
    {
        if (!item.IsDirectory)
        {
            return await PrepareAsync(session, item, cancellationToken);
        }

        var existing = TryGetPrepared(item);
        if (existing?.Succeeded == true && !string.IsNullOrWhiteSpace(existing.LocalPath))
        {
            return existing;
        }

        var linkedCts = ReplaceActivePreparation(cancellationToken);
        var task = _taskService.Start("drag_download_prepare_directory", $"准备拖出文件夹：{item.Name}");
        RegisterActiveTask(task);
        try
        {
            var result = await _downloadService.PrepareDragDownloadDirectoryAsync(
                session,
                item,
                task,
                linkedCts.Token
            );
            RememberPrepared(item, result);
            return result;
        }
        finally
        {
            FinishActivePreparation(linkedCts, task);
        }
    }

    public async Task<FileDragDownloadPreparationResult> PrepareManyAsync(
        WindowsServerSession session,
        IReadOnlyList<DirectoryItemViewModel> items,
        CancellationToken cancellationToken = default
    )
    {
        if (items.Count == 0)
        {
            return new FileDragDownloadPreparationResult(
                false,
                "请先选择要拖出的文件或文件夹。",
                [],
                "download.no_selection"
            );
        }

        if (items.Count == 1)
        {
            var single = await PrepareAsync(session, items[0], cancellationToken, automatic: false);
            return single.Succeeded && !string.IsNullOrWhiteSpace(single.LocalPath)
                ? new FileDragDownloadPreparationResult(
                    true,
                    single.Summary,
                    [new FileDragDownloadPreparedItem(items[0], single.LocalPath)]
                )
                : new FileDragDownloadPreparationResult(false, single.Summary, [], single.ErrorCode);
        }

        var linkedCts = ReplaceActivePreparation(cancellationToken);
        var task = _taskService.Start("drag_download_prepare_many", $"准备拖出 {items.Count} 个项目", items.Count);
        RegisterActiveTask(task);
        var preparedItems = new List<FileDragDownloadPreparedItem>(items.Count);

        try
        {
            for (var index = 0; index < items.Count; index++)
            {
                linkedCts.Token.ThrowIfCancellationRequested();
                task.CancellationToken.ThrowIfCancellationRequested();

                var item = items[index];
                var existing = TryGetPrepared(item);
                FileDownloadResult result;
                if (existing?.Succeeded == true && !string.IsNullOrWhiteSpace(existing.LocalPath))
                {
                    result = existing;
                }
                else
                {
                    result = item.IsDirectory
                        ? await _downloadService.PrepareDragDownloadDirectoryAsync(
                            session,
                            item,
                            task,
                            linkedCts.Token,
                            updateTaskState: false
                        )
                        : await _downloadService.PrepareDragDownloadAsync(
                            session,
                            item,
                            task,
                            linkedCts.Token,
                            updateTaskState: false
                        );
                    RememberPrepared(item, result);
                }

                if (!result.Succeeded || string.IsNullOrWhiteSpace(result.LocalPath))
                {
                    task.Fail(result.Summary, result.ErrorCode);
                    return new FileDragDownloadPreparationResult(
                        false,
                        result.Summary,
                        preparedItems,
                        result.ErrorCode
                    );
                }

                preparedItems.Add(new FileDragDownloadPreparedItem(item, result.LocalPath));
                task.ReportProgress(
                    preparedItems.Count,
                    items.Count,
                    $"准备拖出中：已处理 {preparedItems.Count}/{items.Count} 项。"
                );
            }

            var summary = $"已准备好 {preparedItems.Count} 个项目，可拖出到本地。";
            task.Complete(summary);
            return new FileDragDownloadPreparationResult(true, summary, preparedItems);
        }
        catch (OperationCanceledException)
        {
            task.Cancel("已取消拖出准备。");
            return new FileDragDownloadPreparationResult(
                false,
                "已取消拖出准备。",
                preparedItems,
                "download.cancelled"
            );
        }
        finally
        {
            FinishActivePreparation(linkedCts, task);
        }
    }

    public void Forget(string displayPath)
    {
        lock (_syncRoot)
        {
            _prepared.Remove(displayPath);
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _prepared.Clear();
        }
    }

    public void CancelActive()
    {
        CancellationTokenSource? cts;
        WindowsFileTaskHandle? task;
        lock (_syncRoot)
        {
            cts = _activePreparationCts;
            task = _activeTask;
            _activePreparationCts = null;
            _activeTask = null;
        }

        cts?.Cancel();
        cts?.Dispose();
        task?.Cancel("已取消上一项拖出准备。");
    }

    private void RememberPrepared(DirectoryItemViewModel item, FileDownloadResult result)
    {
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.LocalPath))
        {
            return;
        }

        lock (_syncRoot)
        {
            _prepared[item.DisplayPath] = result;
        }

        _diagnostics.Info($"拖出下载已预热：{item.DisplayPath} -> {result.LocalPath}");
    }

    private CancellationTokenSource ReplaceActivePreparation(CancellationToken cancellationToken)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource? previous;
        WindowsFileTaskHandle? previousTask;
        lock (_syncRoot)
        {
            previous = _activePreparationCts;
            previousTask = _activeTask;
            _activePreparationCts = linkedCts;
            _activeTask = null;
        }

        previous?.Cancel();
        previousTask?.Cancel("已取消上一项拖出准备。");
        return linkedCts;
    }

    private void RegisterActiveTask(WindowsFileTaskHandle task)
    {
        lock (_syncRoot)
        {
            _activeTask = task;
        }
    }

    private void FinishActivePreparation(
        CancellationTokenSource linkedCts,
        WindowsFileTaskHandle task
    )
    {
        lock (_syncRoot)
        {
            if (ReferenceEquals(_activePreparationCts, linkedCts))
            {
                _activePreparationCts = null;
            }
            if (ReferenceEquals(_activeTask, task))
            {
                _activeTask = null;
            }
        }

        linkedCts.Dispose();
    }

    private static bool ShouldAutoPrepare(DirectoryItemViewModel item) =>
        !item.IsDirectory &&
        item.SizeBytes.HasValue &&
        item.SizeBytes.Value <= AutoPrepareMaxBytes;
}
