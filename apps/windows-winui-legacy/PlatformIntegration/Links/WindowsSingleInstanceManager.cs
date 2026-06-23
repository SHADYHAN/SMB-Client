using Rynat.WindowsClient.Infrastructure;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Rynat.WindowsClient.PlatformIntegration.Links;

public sealed class WindowsSingleInstanceManager : IDisposable
{
    private const int Port = 19528;
    private const int MaxCommandBytes = 16 * 1024;
    private const string AckPayload = "RYNAT_SINGLE_INSTANCE_OK";
    private const string MutexName = @"Local\RynatWindowsClientPrimary";

    private readonly WindowsClientDiagnostics _diagnostics;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Queue<SingleInstanceCommand> _pendingCommands = new();
    private readonly object _commandSync = new();
    private Func<SingleInstanceCommand, Task>? _commandReceived;
    private Mutex? _primaryMutex;
    private TcpListener? _listener;
    private Task? _serverLoopTask;

    public WindowsSingleInstanceManager(WindowsClientDiagnostics diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public event Func<SingleInstanceCommand, Task>? CommandReceived
    {
        add
        {
            lock (_commandSync)
            {
                _commandReceived += value;
            }
            _ = FlushPendingCommandsAsync();
        }
        remove
        {
            lock (_commandSync)
            {
                _commandReceived -= value;
            }
        }
    }

    public WindowsSingleInstanceStartupResult TryBecomePrimary()
    {
        try
        {
            _primaryMutex = new Mutex(true, MutexName, out var createdNew);
            if (!createdNew)
            {
                _primaryMutex.Dispose();
                _primaryMutex = null;
                return WindowsSingleInstanceStartupResult.Secondary;
            }

            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start();
            _serverLoopTask = Task.Run(() => RunServerLoopAsync(_cancellationTokenSource.Token));
            _diagnostics.Info($"当前进程已接管 Windows 主客户端单实例通道：127.0.0.1:{Port}");
            return WindowsSingleInstanceStartupResult.Primary;
        }
        catch (SocketException ex)
        {
            _listener?.Stop();
            _listener = null;
            if (_primaryMutex is not null)
            {
                _diagnostics.Error(ex, "单实例 IPC 端口被占用，当前进程仍作为主客户端启动。");
                return WindowsSingleInstanceStartupResult.PrimaryWithoutIpc;
            }

            return WindowsSingleInstanceStartupResult.Secondary;
        }
    }

    public async Task<bool> SendToPrimaryAsync(SingleInstanceCommand command)
    {
        try
        {
            _diagnostics.Info($"开始向主进程转发单实例命令：{command.Kind}");
            using var client = new TcpClient();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(IPAddress.Loopback, Port, timeoutCts.Token).ConfigureAwait(false);
            client.SendTimeout = 2000;
            client.ReceiveTimeout = 2000;
            await using var stream = client.GetStream();
            var payload = JsonSerializer.Serialize(command);
            var bytes = Encoding.UTF8.GetBytes(payload);
            await stream.WriteAsync(bytes, timeoutCts.Token).ConfigureAwait(false);
            await stream.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
            client.Client.Shutdown(SocketShutdown.Send);

            var ackBuffer = new byte[Encoding.UTF8.GetByteCount(AckPayload)];
            var received = 0;
            while (received < ackBuffer.Length)
            {
                var read = await stream
                    .ReadAsync(ackBuffer.AsMemory(received), timeoutCts.Token)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                received += read;
            }

            var ack = Encoding.UTF8.GetString(ackBuffer, 0, received);
            if (!string.Equals(ack, AckPayload, StringComparison.Ordinal))
            {
                _diagnostics.Info($"单实例命令转发未收到主进程确认：{command.Kind}");
                return false;
            }

            _diagnostics.Info($"已将单实例命令转发给主进程：{command.Kind}");
            return true;
        }
        catch (Exception ex)
        {
            _diagnostics.Error(ex, $"单实例命令转发失败：{command.Kind}");
            return false;
        }
    }

    private async Task RunServerLoopAsync(CancellationToken cancellationToken)
    {
        if (_listener is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                client.ReceiveTimeout = 2000;
                using var stream = client.GetStream();
                using var memory = new MemoryStream();
                await CopyLimitedAsync(stream, memory, MaxCommandBytes, cancellationToken);
                var payload = Encoding.UTF8.GetString(memory.ToArray());
                if (string.IsNullOrWhiteSpace(payload))
                {
                    continue;
                }

                var command = JsonSerializer.Deserialize<SingleInstanceCommand>(payload);
                if (command is not null)
                {
                    _diagnostics.Info($"主进程收到单实例命令：{command.Kind}");
                    await DispatchCommandAsync(command);
                    await SendAckAsync(stream, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _diagnostics.Error(ex, "单实例命令通道处理失败");
            }
        }
    }

    private static async Task SendAckAsync(
        NetworkStream stream,
        CancellationToken cancellationToken
    )
    {
        var ackBytes = Encoding.UTF8.GetBytes(AckPayload);
        await stream.WriteAsync(ackBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private async Task DispatchCommandAsync(SingleInstanceCommand command)
    {
        Func<SingleInstanceCommand, Task>? handler;
        lock (_commandSync)
        {
            handler = _commandReceived;
            if (handler is null)
            {
                _pendingCommands.Enqueue(command);
                return;
            }
        }

        await handler.Invoke(command);
    }

    private async Task FlushPendingCommandsAsync()
    {
        while (true)
        {
            SingleInstanceCommand? command = null;
            Func<SingleInstanceCommand, Task>? handler;
            lock (_commandSync)
            {
                handler = _commandReceived;
                if (handler is null || _pendingCommands.Count == 0)
                {
                    return;
                }

                command = _pendingCommands.Dequeue();
            }

            await handler.Invoke(command);
        }
    }

    private static async Task CopyLimitedAsync(
        Stream source,
        Stream destination,
        int maxBytes,
        CancellationToken cancellationToken
    )
    {
        var buffer = new byte[4096];
        var total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return;
            }

            total += read;
            if (total > maxBytes)
            {
                throw new InvalidOperationException("单实例命令过大。");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        try
        {
            _listener?.Stop();
        }
        catch
        {
        }

        try
        {
            _serverLoopTask?.Wait(1000);
        }
        catch
        {
        }

        try
        {
            _primaryMutex?.ReleaseMutex();
        }
        catch
        {
        }

        _primaryMutex?.Dispose();
        _cancellationTokenSource.Dispose();
    }
}

public sealed record SingleInstanceCommand(string Kind, string? RawLink)
{
    public static SingleInstanceCommand ActivateWindow() => new("activate", null);

    public static SingleInstanceCommand OpenLink(string rawLink) => new("open_link", rawLink);
}

public enum WindowsSingleInstanceStartupResult
{
    Primary,
    PrimaryWithoutIpc,
    Secondary
}
