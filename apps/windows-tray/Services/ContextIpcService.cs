using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rynat.WindowsTray.App;

namespace Rynat.WindowsTray.Services;

internal sealed class ContextIpcService : IDisposable
{
    private const int ContextPort = 19528;
    private readonly ShellState _state;
    private readonly ShareLinkService _shareLinkService;
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;

    public ContextIpcService(ShellState state, ShareLinkService shareLinkService)
    {
        _state = state;
        _shareLinkService = shareLinkService;
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
        if (!string.Equals(context.Request.Url?.AbsolutePath, "/context", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(context.Response, 404, ContextResponse.Failed("not found"));
            return;
        }

        try
        {
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
                await WriteJsonAsync(context.Response, 400, ContextResponse.Failed("请先登录 RYNAT。"));
                return;
            }

            if (!IsPathUnderCurrentServer(request.Path))
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

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, ContextResponse payload)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private bool IsPathUnderCurrentServer(string path)
    {
        if (string.IsNullOrWhiteSpace(_state.ServerHost))
        {
            return false;
        }

        var normalizedPath = path.Trim().Replace('/', '\\');
        var serverRoot = $@"\\{_state.ServerHost.Trim().Trim('\\')}";
        return normalizedPath.Equals(serverRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(serverRoot + @"\", StringComparison.OrdinalIgnoreCase);
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
