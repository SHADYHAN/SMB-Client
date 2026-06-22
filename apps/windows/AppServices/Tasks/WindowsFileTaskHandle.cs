using Rynat.Client;
using Rynat.WindowsClient.Infrastructure;

namespace Rynat.WindowsClient.AppServices.Tasks;

public sealed class WindowsFileTaskHandle : IDisposable
{
    private readonly WindowsFileTaskService _owner;
    private readonly CancellationTokenSource _cancellationSource;
    private readonly object _syncRoot = new();
    private bool _disposed;

    internal WindowsFileTaskHandle(
        WindowsFileTaskService owner,
        string id,
        string kind,
        string title,
        int? totalItems,
        string? coreOperationId,
        CancellationTokenSource cancellationSource
    )
    {
        _owner = owner;
        Id = id;
        Kind = kind;
        Title = title;
        TotalItems = totalItems;
        CoreOperationId = coreOperationId;
        _cancellationSource = cancellationSource;
        StartedAt = DateTimeOffset.Now;
        State = WindowsFileTaskState.Pending;
        Summary = "等待开始。";
    }

    public string Id { get; }

    public string Kind { get; }

    public string Title { get; }

    public int CompletedItems { get; private set; }

    public int? TotalItems { get; private set; }

    public string Summary { get; private set; }

    public string? ErrorCode { get; private set; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public WindowsFileTaskState State { get; private set; }

    public string? CoreOperationId { get; }

    public CancellationToken CancellationToken => _cancellationSource.Token;

    public WindowsFileTaskSnapshot Snapshot()
    {
        lock (_syncRoot)
        {
            return BuildSnapshot();
        }
    }

    public void Start(string? summary = null)
    {
        WindowsFileTaskSnapshot? snapshot = null;
        lock (_syncRoot)
        {
            if (IsTerminalState(State))
            {
                return;
            }

            State = WindowsFileTaskState.Running;
            Summary = string.IsNullOrWhiteSpace(summary) ? "正在处理..." : summary;
            snapshot = BuildSnapshot();
        }

        _owner.PublishChanged(snapshot);
    }

    public void ReportProgress(
        int completedItems,
        int? totalItems = null,
        string? summary = null
    )
    {
        WindowsFileTaskSnapshot? snapshot = null;
        lock (_syncRoot)
        {
            if (IsTerminalState(State))
            {
                return;
            }

            State = WindowsFileTaskState.Running;
            CompletedItems = Math.Max(0, completedItems);
            if (totalItems.HasValue)
            {
                TotalItems = Math.Max(0, totalItems.Value);
            }

            if (!string.IsNullOrWhiteSpace(summary))
            {
                Summary = summary;
            }

            snapshot = BuildSnapshot();
        }

        _owner.PublishChanged(snapshot);
    }

    public void Complete(string summary)
    {
        WindowsFileTaskSnapshot? snapshot = null;
        lock (_syncRoot)
        {
            if (IsTerminalState(State))
            {
                return;
            }

            State = WindowsFileTaskState.Completed;
            CompletedAt = DateTimeOffset.Now;
            Summary = string.IsNullOrWhiteSpace(summary) ? "任务已完成。" : summary;
            ErrorCode = null;
            snapshot = BuildSnapshot();
        }

        _owner.PublishChanged(snapshot);
    }

    public void Fail(string summary, string? errorCode = null)
    {
        WindowsFileTaskSnapshot? snapshot = null;
        lock (_syncRoot)
        {
            if (IsTerminalState(State))
            {
                return;
            }

            State = WindowsFileTaskState.Failed;
            CompletedAt = DateTimeOffset.Now;
            Summary = string.IsNullOrWhiteSpace(summary) ? "任务失败。" : summary;
            ErrorCode = errorCode ?? "task.failed";
            snapshot = BuildSnapshot();
        }

        _owner.PublishChanged(snapshot);
    }

    public void Cancel(string? summary = null)
    {
        WindowsFileTaskSnapshot? snapshot = null;
        var shouldCancel = false;
        lock (_syncRoot)
        {
            if (IsTerminalState(State))
            {
                return;
            }

            shouldCancel = true;
            State = WindowsFileTaskState.Cancelled;
            CompletedAt = DateTimeOffset.Now;
            Summary = string.IsNullOrWhiteSpace(summary) ? "任务已取消。" : summary;
            ErrorCode = "task.cancelled";
            snapshot = BuildSnapshot();
        }

        if (shouldCancel)
        {
            _cancellationSource.Cancel();
        }

        TryCancelCoreOperation();
        _owner.PublishChanged(snapshot);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellationSource.Dispose();
    }

    private WindowsFileTaskSnapshot BuildSnapshot() =>
        new(
            Id,
            Kind,
            Title,
            State,
            CompletedItems,
            TotalItems,
            Summary,
            ErrorCode,
            StartedAt,
            CompletedAt,
            CoreOperationId
        );

    private void TryCancelCoreOperation()
    {
        if (string.IsNullOrWhiteSpace(CoreOperationId))
        {
            return;
        }

        try
        {
            _owner.Bridge.SmbCancelOperation(new SmbCancelOperationRequest(CoreOperationId));
        }
        catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
        {
            _owner.Diagnostics.Error(ex, $"取消 core 操作失败：{CoreOperationId}");
        }
    }

    private static bool IsTerminalState(WindowsFileTaskState state) =>
        state is WindowsFileTaskState.Completed
            or WindowsFileTaskState.Failed
            or WindowsFileTaskState.Cancelled;
}
