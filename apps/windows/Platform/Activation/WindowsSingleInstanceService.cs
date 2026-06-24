using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Rynat.WindowsClient.Platform.Activation;

public sealed class WindowsSingleInstanceService : IAppSingleInstanceService
{
    private const string MutexName = "Local\\RYNAT.SharedDrive.Client";
    private const string PipeName = "RYNAT.SharedDrive.Client.Activation";
    private readonly Mutex _mutex = new(false, MutexName);
    private CancellationTokenSource? _listenerCancellation;
    private Task? _listenerTask;
    private bool _disposed;

    public event EventHandler<ExternalActivationEventArgs>? Activated;

    public bool IsPrimaryInstance { get; private set; }

    public async Task<bool> StartAsync(
        IReadOnlyList<string> startupArguments,
        CancellationToken cancellationToken = default
    )
    {
        IsPrimaryInstance = _mutex.WaitOne(0);
        if (!IsPrimaryInstance)
        {
            await ForwardAsync(startupArguments, cancellationToken);
            return false;
        }

        _listenerCancellation = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenAsync(_listenerCancellation.Token), CancellationToken.None);
        return true;
    }

    public void Stop()
    {
        _listenerCancellation?.Cancel();
        _listenerCancellation?.Dispose();
        _listenerCancellation = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        if (IsPrimaryInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
        _disposed = true;
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous
            );

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                var payload = await reader.ReadToEndAsync(cancellationToken);
                var arguments = JsonSerializer.Deserialize<string[]>(payload) ?? Array.Empty<string>();
                Activated?.Invoke(this, new ExternalActivationEventArgs(arguments));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Activation is best-effort. A malformed second launch should not close the primary window.
            }
        }
    }

    private static async Task ForwardAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        var payload = JsonSerializer.Serialize(arguments);
        var bytes = Encoding.UTF8.GetBytes(payload);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(
                    ".",
                    PipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous
                );
                await pipe.ConnectAsync(500, cancellationToken);
                await pipe.WriteAsync(bytes, cancellationToken);
                await pipe.FlushAsync(cancellationToken);
                return;
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(150, cancellationToken);
            }
        }
    }
}
