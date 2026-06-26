using System.Text.Json;
using Rynat.Client;
using Rynat.WindowsClient.Infrastructure;

namespace Rynat.WindowsClient.Services.Smb;

public sealed class SmbTaskService : ISmbTaskService
{
    private static readonly TimeSpan InitialPollDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan MaxPollDelay = TimeSpan.FromMilliseconds(750);
    private readonly RynatCoreBridge _bridge;

    public SmbTaskService(RynatCoreBridge bridge)
    {
        _bridge = bridge;
    }

    public async Task<JsonElement?> RunAsync(
        string operation,
        JsonElement payload,
        string? operationId = null,
        string? serverProfileId = null,
        bool useIsolatedConnection = false,
        CancellationToken cancellationToken = default
    )
    {
        var start = _bridge.SmbStartTask(new SmbStartTaskRequest(
            operation,
            payload,
            operationId,
            serverProfileId,
            useIsolatedConnection
        ));

        using var cancellationRegistration = cancellationToken.Register(() => CancelQuietly(start.TaskId));
        try
        {
            var delay = InitialPollDelay;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var status = _bridge.SmbPollTask(new SmbTaskRequest(start.TaskId));
                if (IsTerminal(status.State))
                {
                    return Complete(status);
                }

                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(Math.Min(
                    MaxPollDelay.TotalMilliseconds,
                    delay.TotalMilliseconds * 1.4
                ));
            }
        }
        catch (OperationCanceledException)
        {
            CancelQuietly(start.TaskId);
            throw;
        }
        finally
        {
            ClearQuietly(start.TaskId);
        }
    }

    private JsonElement? Complete(SmbTaskStatus status)
    {
        if (IsSucceeded(status.State))
        {
            return status.Data;
        }

        if (IsCancelled(status.State))
        {
            throw new OperationCanceledException(status.Error ?? "操作已取消。");
        }

        throw new RynatCoreBridgeException(
            status.Error ?? "SMB 任务失败。",
            status.ErrorCode
        );
    }

    private void CancelQuietly(string taskId)
    {
        try
        {
            _bridge.SmbCancelTask(new SmbTaskRequest(taskId));
        }
        catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
        {
        }
    }

    private void ClearQuietly(string taskId)
    {
        try
        {
            _bridge.SmbClearTask(new SmbTaskRequest(taskId));
        }
        catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
        {
        }
    }

    private static bool IsTerminal(string state) =>
        IsSucceeded(state) || IsCancelled(state) || state.Equals("failed", StringComparison.OrdinalIgnoreCase);

    private static bool IsSucceeded(string state) =>
        state.Equals("succeeded", StringComparison.OrdinalIgnoreCase);

    private static bool IsCancelled(string state) =>
        state.Equals("cancelled", StringComparison.OrdinalIgnoreCase);
}
