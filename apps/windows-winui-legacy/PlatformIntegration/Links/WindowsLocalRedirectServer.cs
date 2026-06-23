using Rynat.Client;
using Rynat.WindowsClient.Infrastructure;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Rynat.WindowsClient.PlatformIntegration.Links;

public sealed class WindowsLocalRedirectServer : IDisposable
{
    private const int Port = 19527;
    private const int MaxHttpRequestBytes = 64 * 1024;
    private const string HelperMutexName = @"Local\RynatWindowsLinkRedirectHelper";

    private readonly RynatCoreBridge _bridge;
    private readonly WindowsClientDiagnostics _diagnostics;
    private readonly string _clientExecutablePath;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Mutex? _helperMutex;
    private TcpListener? _listener;
    private Task? _serverLoopTask;

    public WindowsLocalRedirectServer(
        RynatCoreBridge bridge,
        WindowsClientDiagnostics diagnostics,
        string clientExecutablePath
    )
    {
        _bridge = bridge;
        _diagnostics = diagnostics;
        _clientExecutablePath = clientExecutablePath;
        _helperMutex = new Mutex(true, HelperMutexName, out var createdNew);
        IsPrimaryHelperInstance = createdNew;
    }

    public bool IsPrimaryHelperInstance { get; }

    public bool Start()
    {
        if (!IsPrimaryHelperInstance)
        {
            _diagnostics.Info("本地链接助手已在运行，当前 helper 进程将退出。");
            return false;
        }

        try
        {
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start();
            _serverLoopTask = Task.Run(() => RunServerLoopAsync(_cancellationTokenSource.Token));
            _diagnostics.Info($"本地链接助手开始监听：http://127.0.0.1:{Port}");
            return true;
        }
        catch (Exception ex)
        {
            _diagnostics.Error(ex, "启动本地链接助手失败");
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
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _diagnostics.Error(ex, "本地链接助手接受连接失败");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using var disposableClient = client;
            client.ReceiveTimeout = 2000;
            client.SendTimeout = 2000;
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(2));
            var request = await ReadHttpRequestAsync(reader, requestTimeout.Token);
            if (string.IsNullOrWhiteSpace(request))
            {
                return;
            }

            var firstLine = request.Split("\r\n", StringSplitOptions.None).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(firstLine))
            {
                return;
            }

            var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return;
            }

            var rawPath = parts[1];
            var headers = ParseHeaders(request);
            if (!IsAllowedLocalRequest(headers))
            {
                await SendResponseAsync(stream, "403 Forbidden", "text/plain; charset=utf-8", "Forbidden", cancellationToken);
                return;
            }

            if (!IsShareRedirectPath(rawPath))
            {
                await SendResponseAsync(stream, "404 Not Found", "text/plain; charset=utf-8", "Not Found", cancellationToken);
                return;
            }

            var queryIndex = rawPath.IndexOf('?');
            var query = queryIndex >= 0 && queryIndex < rawPath.Length - 1
                ? rawPath[(queryIndex + 1)..]
                : string.Empty;
            var target = "rynat://s" + (string.IsNullOrWhiteSpace(query) ? string.Empty : "?" + query);
            if (!IsAllowedTarget(query))
            {
                await SendResponseAsync(stream, "403 Forbidden", "text/plain; charset=utf-8", "Forbidden", cancellationToken);
                return;
            }

            _diagnostics.Info($"HTTP 链接命中本地助手，准备唤起客户端：{target}");

            LaunchClient(target);

            await SendResponseAsync(
                stream,
                "200 OK",
                "text/html; charset=utf-8",
                BuildRedirectPage(target),
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _diagnostics.Info("本地链接助手读取请求超时，已关闭连接。");
        }
        catch (Exception ex)
        {
            _diagnostics.Error(ex, "本地链接助手处理请求失败");
        }
    }

    private void LaunchClient(string target)
    {
        try
        {
            var startInfo = new ProcessStartInfo(_clientExecutablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("--open-link");
            startInfo.ArgumentList.Add(target);
            using var process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            _diagnostics.Error(ex, "本地链接助手唤起客户端失败");
        }
    }

    private string BuildRedirectPage(string target)
    {
        var escaped = WebUtility.HtmlEncode(target);
        return $"""
        <!doctype html>
        <html>
        <head><meta charset="utf-8"><title>RYNAT 共享网盘</title></head>
        <body>
          <p>正在打开 RYNAT 共享网盘。</p>
          <p>如果客户端没有响应，请确认客户端已安装并正在运行。</p>
          <p><a href="{escaped}">重试打开</a></p>
        </body>
        </html>
        """;
    }

    private static bool IsShareRedirectPath(string rawPath)
    {
        var queryIndex = rawPath.IndexOf('?');
        var pathOnly = queryIndex >= 0 ? rawPath[..queryIndex] : rawPath;
        return pathOnly.Equals("/s", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadHttpRequestAsync(
        StreamReader reader,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder();
        var readBytes = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            builder.Append(line).Append("\r\n");
            readBytes += Encoding.UTF8.GetByteCount(line) + 2;
            if (readBytes > MaxHttpRequestBytes)
            {
                throw new InvalidOperationException("HTTP 请求过大。");
            }

            if (line.Length == 0)
            {
                break;
            }
        }

        return builder.ToString();
    }

    private static Dictionary<string, string> ParseHeaders(string request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = request.Split("\r\n", StringSplitOptions.None);
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                break;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return headers;
    }

    private static bool IsAllowedLocalRequest(IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Host", out var host)
            || !IsLocalRedirectHost(host))
        {
            return false;
        }

        if (headers.TryGetValue("Origin", out var origin)
            && !IsLocalRedirectOrigin(origin))
        {
            return false;
        }

        if (headers.TryGetValue("Sec-Fetch-Site", out var fetchSite)
            && fetchSite.Equals("cross-site", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsLocalRedirectHost(string host)
    {
        var normalized = host.Trim().TrimEnd('.');
        return normalized.Equals($"127.0.0.1:{Port}", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals($"localhost:{Port}", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals($"[::1]:{Port}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLocalRedirectOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Port == Port
            && (uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase));
    }

    private bool IsAllowedTarget(string query)
    {
        var parameters = ParseQuery(query);
        if (!parameters.TryGetValue("h", out var rawHost)
            || string.IsNullOrWhiteSpace(rawHost)
            || !TryParseLinkHost(rawHost, out var targetHost, out var targetPort))
        {
            return false;
        }

        try
        {
            var snapshot = _bridge.AppBootstrap();
            return snapshot.ServerProfiles.Any(profile =>
                EndpointMatches(profile.Endpoint, targetHost, targetPort)
            );
        }
        catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
        {
            _diagnostics.Error(ex, "校验本地链接目标失败");
            return false;
        }
    }

    private static bool TryParseLinkHost(string rawHost, out string host, out int? port)
    {
        host = string.Empty;
        port = null;

        var value = rawHost.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Contains("://", StringComparison.Ordinal)
            ? value
            : "smb://" + value;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        host = uri.Host.TrimEnd('.');
        port = uri.IsDefaultPort ? null : uri.Port;
        return true;
    }

    private static bool EndpointMatches(StoredServerEndpoint endpoint, string targetHost, int? targetPort)
    {
        if (!endpoint.Host.TrimEnd('.').Equals(targetHost.TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return PortsMatch(endpoint.Port, targetPort);
    }

    private static bool PortsMatch(ushort? profilePort, int? targetPort)
    {
        var normalizedProfilePort = NormalizeDefaultSmbPort(profilePort);
        var normalizedTargetPort = NormalizeDefaultSmbPort(targetPort);
        return normalizedProfilePort == normalizedTargetPort;
    }

    private static int NormalizeDefaultSmbPort(int? port) => port is null or 445 ? 445 : port.Value;

    private static int NormalizeDefaultSmbPort(ushort? port) => port is null or 445 ? 445 : port.Value;

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator >= 0 ? pair[..separator] : pair;
            var value = separator >= 0 ? pair[(separator + 1)..] : string.Empty;
            values[WebUtility.UrlDecode(key)] = WebUtility.UrlDecode(value);
        }

        return values;
    }

    private static async Task SendResponseAsync(
        NetworkStream stream,
        string status,
        string contentType,
        string body,
        CancellationToken cancellationToken
    )
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var headers = string.Join(
            "\r\n",
            [
                $"HTTP/1.1 {status}",
                $"Content-Type: {contentType}",
                "Cache-Control: no-store",
                $"Content-Length: {bodyBytes.Length}",
                "Connection: close",
                string.Empty,
                string.Empty
            ]
        );

        var headerBytes = Encoding.UTF8.GetBytes(headers);
        await stream.WriteAsync(headerBytes, cancellationToken);
        await stream.WriteAsync(bodyBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
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

        if (IsPrimaryHelperInstance)
        {
            _helperMutex?.ReleaseMutex();
        }

        _helperMutex?.Dispose();
        _cancellationTokenSource.Dispose();
    }
}
