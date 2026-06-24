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
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private Task? _serverTask;
    private bool _disposed;
    private readonly RynatCoreBridge _bridge;

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
                var html = BuildAcceptedPage(deepLink);
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

        var path = uri.AbsolutePath.Trim('/');
        if (!string.Equals(path, "s", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("s/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var payloadPath = path.Length > 2 ? path[1..] : string.Empty;
        deepLink = "rynat://s" + payloadPath + uri.Query;
        return true;
    }

    private string BuildAcceptedPage(string deepLink)
    {
        try
        {
            return _bridge.RedirectPage(new RedirectPageRequest(deepLink));
        }
        catch
        {
            return BuildFallbackPage(deepLink);
        }
    }

    private static string BuildFallbackPage(string deepLink)
    {
        var escapedUrl = WebUtility.HtmlEncode(deepLink);
        var jsUrl = System.Text.Json.JsonSerializer.Serialize(deepLink);
        return $$"""
<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>RYNAT 共享网盘</title>
<style>
html,body{margin:0;width:100%;height:100%;}
body{display:none;font-family:"Segoe UI","Microsoft YaHei",sans-serif;background:#f6f7f9;color:#1f2933;}
.fallback{box-sizing:border-box;width:min(360px,calc(100vw - 48px));margin:auto;padding:24px;border:1px solid #d9dee7;background:#fff;box-shadow:0 18px 50px rgba(31,41,51,.12);}
h1{margin:0 0 8px;font-size:17px;font-weight:650;}
p{margin:0 0 16px;color:#667085;font-size:13px;line-height:1.6;}
a{height:34px;padding:0 14px;display:inline-flex;align-items:center;justify-content:center;text-decoration:none;font-size:13px;color:#fff;background:#1f2933;}
</style>
</head>
<body>
<main class="fallback">
  <h1>正在打开 RYNAT 共享网盘</h1>
  <p>如果窗口没有自动显示，可以点击重试。</p>
  <a href="{{escapedUrl}}">重试打开</a>
</main>
<script>
(function(){
  var url = {{jsUrl}};
  function closeTab(){
    try { window.open('', '_self'); window.close(); } catch (_) {}
  }
  setTimeout(closeTab, 80);
  setTimeout(closeTab, 320);
  setTimeout(function(){ document.body.style.display = 'flex'; }, 1200);
})();
</script>
</body>
</html>
""";
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
