using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rynat.WindowsTray.App;

namespace Rynat.WindowsTray.Services;

internal sealed class ContextIpcService : IDisposable
{
    private const int ContextPort = 19528;
    private const long MaxRequestBytes = 32 * 1024;
    private readonly ShellState _state;
    private readonly ShareLinkService _shareLinkService;
    private readonly string _contextToken;
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;

    public ContextIpcService(ShellState state, ShareLinkService shareLinkService)
    {
        _state = state;
        _shareLinkService = shareLinkService;
        _contextToken = ContextIpcSecurity.GetToken();
        _listener.Prefixes.Add($"http://127.0.0.1:{ContextPort}/");
    }

    public void Start()
    {
        try
        {
            _cts = new CancellationTokenSource();
            _listener.Start();
            _state.ContextIpcRunning = true;
            _state.ContextIpcStatus = $"监听 127.0.0.1:{ContextPort}";
            _ = Task.Run(() => ListenAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            _state.ContextIpcRunning = false;
            _state.ContextIpcStatus = ex.Message;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        if (_listener.IsListening)
        {
            _listener.Stop();
        }

        _listener.Close();
        _cts?.Dispose();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleAsync(context), cancellationToken);
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(context.Response, 405, ContextResponse.Failed("method not allowed"));
            return;
        }

        if (!string.Equals(context.Request.Url?.AbsolutePath, "/context", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(context.Response, 404, ContextResponse.Failed("not found"));
            return;
        }

        try
        {
            if (!IPAddress.IsLoopback(context.Request.RemoteEndPoint?.Address ?? IPAddress.None))
            {
                await WriteJsonAsync(context.Response, 403, ContextResponse.Failed("forbidden"));
                return;
            }

            if (!IsAuthorized(context.Request))
            {
                await WriteJsonAsync(context.Response, 403, ContextResponse.Failed("forbidden"));
                return;
            }

            if (context.Request.ContentLength64 < 0 || context.Request.ContentLength64 > MaxRequestBytes)
            {
                await WriteJsonAsync(context.Response, 413, ContextResponse.Failed("request too large"));
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            var body = await reader.ReadToEndAsync();
            var request = JsonSerializer.Deserialize<ContextRequest>(body, JsonOptions);
            if (request is null || !string.Equals(request.Action, "copy_link", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(request.Path))
            {
                await WriteJsonAsync(context.Response, 400, ContextResponse.Failed("invalid context request"));
                return;
            }

            if (!_state.Connected)
            {
                await WriteJsonAsync(context.Response, 400, ContextResponse.Failed("请先登录 RYANT共享网盘。"));
                return;
            }

            if (!UncPathPolicy.IsPathUnderServer(request.Path, _state.ServerHost))
            {
                await WriteJsonAsync(context.Response, 400, ContextResponse.Failed("只能为当前登录服务器下的 UNC 路径复制分享链接。"));
                return;
            }

            var link = _shareLinkService.CreateShareLink(request.Path, request.Kind);
            await ClipboardService.SetTextAsync(link);
            _state.LastActivation = $"右键复制链接: {request.Path}";
            await WriteJsonAsync(context.Response, 200, ContextResponse.Copied(link));
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(context.Response, 500, ContextResponse.Failed(ex.Message));
        }
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        return string.Equals(
            request.Headers[ContextIpcSecurity.HeaderName],
            _contextToken,
            StringComparison.Ordinal);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, ContextResponse payload)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class ContextRequest
    {
        public string Action { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public string Kind { get; set; } = "file";
    }

    private sealed class ContextResponse
    {
        public bool Ok { get; init; }

        [JsonPropertyName("http_url")]
        public string? HttpUrl { get; init; }

        public string? Message { get; init; }

        public static ContextResponse Copied(string httpUrl)
        {
            return new ContextResponse
            {
                Ok = true,
                HttpUrl = httpUrl,
                Message = "分享链接已复制"
            };
        }

        public static ContextResponse Failed(string message)
        {
            return new ContextResponse
            {
                Ok = false,
                Message = message
            };
        }
    }
}
