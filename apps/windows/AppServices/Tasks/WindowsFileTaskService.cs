using Rynat.Client;
using Rynat.WindowsClient.Infrastructure;

namespace Rynat.WindowsClient.AppServices.Tasks;

public sealed class WindowsFileTaskService
{
    private const int MaxTaskHistory = 64;
    private readonly Dictionary<string, WindowsFileTaskHandle> _tasks = new();
    private readonly object _syncRoot = new();

    public WindowsFileTaskService(
        RynatCoreBridge bridge,
        WindowsClientDiagnostics diagnostics
    )
    {
        Bridge = bridge;
        Diagnostics = diagnostics;
    }

    internal RynatCoreBridge Bridge { get; }

    internal WindowsClientDiagnostics Diagnostics { get; }

    public event EventHandler<WindowsFileTaskSnapshot>? TaskChanged;

    public WindowsFileTaskHandle Start(
        string kind,
        string title,
        int? totalItems = null,
        string? coreOperationId = null
    )
    {
        var id = Guid.NewGuid().ToString("N");
        var operationId = string.IsNullOrWhiteSpace(coreOperationId)
            ? $"windows-{id}"
            : coreOperationId;
        var cancellationSource = new CancellationTokenSource();
        var handle = new WindowsFileTaskHandle(
            this,
            id,
            kind,
            title,
            totalItems,
            operationId,
            cancellationSource
        );

        lock (_syncRoot)
        {
            _tasks[id] = handle;
        }

        PublishChanged(handle.Snapshot());
        handle.Start();
        return handle;
    }

    public bool Cancel(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return false;
        }

        WindowsFileTaskHandle? handle;
        lock (_syncRoot)
        {
            _tasks.TryGetValue(taskId, out handle);
        }

        if (handle is null)
        {
            return false;
        }

        handle.Cancel();
        return true;
    }

    public int CancelAll()
    {
        WindowsFileTaskHandle[] handles;
        lock (_syncRoot)
        {
            handles = _tasks.Values.ToArray();
        }

        foreach (var handle in handles)
        {
            handle.Cancel();
        }

        return handles.Length;
    }

    public IReadOnlyList<WindowsFileTaskSnapshot> ListSnapshots()
    {
        WindowsFileTaskHandle[] handles;
        lock (_syncRoot)
        {
            handles = _tasks.Values.ToArray();
        }

        return handles
            .Select(task => task.Snapshot())
            .OrderByDescending(snapshot => snapshot.StartedAt)
            .ToArray();
    }

    public bool Forget(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return false;
        }

        WindowsFileTaskHandle? handle;
        lock (_syncRoot)
        {
            if (!_tasks.Remove(taskId, out handle))
            {
                return false;
            }
        }

        handle.Dispose();
        return true;
    }

    internal void PublishChanged(WindowsFileTaskSnapshot snapshot)
    {
        TaskChanged?.Invoke(this, snapshot);
        TrimCompletedTasks();
    }

    internal void RemoveCompletedTask(string taskId)
    {
        WindowsFileTaskHandle? handle;
        lock (_syncRoot)
        {
            _tasks.TryGetValue(taskId, out handle);
        }

        if (handle?.Snapshot().CompletedAt is null)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (!_tasks.Remove(taskId, out var current))
            {
                handle = null;
                return;
            }

            handle = current;
        }

        handle?.Dispose();
    }

    private void TrimCompletedTasks()
    {
        WindowsFileTaskHandle[] toRemove;
        WindowsFileTaskHandle[] handles;
        lock (_syncRoot)
        {
            handles = _tasks.Values.ToArray();
        }

        toRemove = handles
            .Select(task => new { Task = task, Snapshot = task.Snapshot() })
            .Where(entry => entry.Snapshot.CompletedAt is not null)
            .OrderByDescending(entry => entry.Snapshot.CompletedAt)
            .Skip(MaxTaskHistory)
            .Select(entry => entry.Task)
            .ToArray();

        lock (_syncRoot)
        {
            foreach (var task in toRemove)
            {
                _tasks.Remove(task.Id);
            }
        }

        foreach (var task in toRemove)
        {
            task.Dispose();
        }
    }
}
