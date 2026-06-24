using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Rynat.Client;

namespace Rynat.WindowsClient.Platform.Activation;

public sealed class LocalLinkRedirectService : ILocalLinkRedirectService
{
    private const int Port = 19527;
    private const int MaxRequestLineBytes = 8192;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    private readonly RynatCoreBridge _bridge;
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private Task? _serverTask;
    private bool _disposed;

    public LocalLinkRedirectService(RynatCoreBridge bridge)
    {
        _bridge = bridge;
    }

    public event EventHandler<ExternalActivationEventArgs>? Activated;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_serverTask is not null)
        {
            return Task.CompletedTask;
        }

        try
        {
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
        }
        catch
        {
            _listener = null;
            return Task.CompletedTask;
        }

        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _serverTask = Task.Run(() => AcceptLoopAsync(_cancellation.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        _listener?.Stop();
        _listener = null;
        _serverTask = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (_listener is null)
                {
                    break;
                }

                continue;
            }

            _ = Task.Run(() => HandleClientAsync(client), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        using (var timeout = new CancellationTokenSource(RequestTimeout))
        {
            try
            {
                var stream = client.GetStream();
                var requestLine = await ReadRequestLineAsync(stream, timeout.Token);
                if (requestLine is null || !TryBuildDeepLink(requestLine, out var deepLink))
                {
                    await WriteResponseAsync(stream, "404 Not Found", "text/plain; charset=utf-8", "Not Found", timeout.Token);
                    return;
                }

                Activated?.Invoke(this, new ExternalActivationEventArgs(new[] { deepLink }));
                var html = _bridge.RedirectPage(new RedirectPageRequest(deepLink));
                await WriteResponseAsync(stream, "200 OK", "text/html; charset=utf-8", html, timeout.Token);
            }
            catch
            {
                // Local redirect exists only to wake the client from browser links.
            }
        }
    }

    private static async Task<string?> ReadRequestLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var requestBytes = new MemoryStream();
        var buffer = new byte[512];

        while (requestBytes.Length < MaxRequestLineBytes)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count <= 0)
            {
                break;
            }

            requestBytes.Write(buffer, 0, count);
            var request = Encoding.ASCII.GetString(requestBytes.ToArray());
            var firstLineEnd = request.IndexOf("\r\n", StringComparison.Ordinal);
            if (firstLineEnd > 0)
            {
                return request[..firstLineEnd];
            }
        }

        return requestBytes.Length > 0 ? Encoding.ASCII.GetString(requestBytes.ToArray()) : null;
    }

    private static bool TryBuildDeepLink(string requestLine, out string deepLink)
    {
        deepLink = string.Empty;
        var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !string.Equals(parts[0], "GET", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Uri.TryCreate($"http://127.0.0.1:{Port}{parts[1]}", UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.AbsolutePath.Trim('/'), "s", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = "rynat",
            Host = "s",
            Port = -1,
            Path = string.Empty
        };
        deepLink = builder.Uri.ToString();
        return true;
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        string status,
        string contentType,
        string body,
        CancellationToken cancellationToken
    )
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var header = $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, cancellationToken);
        await stream.WriteAsync(bodyBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
