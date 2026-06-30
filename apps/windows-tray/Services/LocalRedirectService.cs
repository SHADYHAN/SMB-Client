using System.Net;
using System.Text;
using Rynat.WindowsTray.App;

namespace Rynat.WindowsTray.Services;

internal sealed class LocalRedirectService : IDisposable
{
    private readonly ShellState _state;
    private readonly ExplorerService _explorerService;
    private readonly ShareLinkService _shareLinkService = new();
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;

    public LocalRedirectService(ShellState state, ExplorerService explorerService)
    {
        _state = state;
        _explorerService = explorerService;
        _listener.Prefixes.Add($"http://127.0.0.1:{ShareLinkService.LocalRedirectPort}/");
    }

    public void Start()
    {
        try
        {
            _cts = new CancellationTokenSource();
            _listener.Start();
            _state.LocalRedirectRunning = true;
            _state.LocalRedirectStatus = $"监听 127.0.0.1:{ShareLinkService.LocalRedirectPort}";
            _ = Task.Run(() => ListenAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            _state.LocalRedirectRunning = false;
            _state.LocalRedirectStatus = ex.Message;
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
        var path = context.Request.Url?.AbsolutePath ?? "/";
        if (!path.StartsWith("/s/", StringComparison.OrdinalIgnoreCase))
        {
            await WriteHtmlAsync(context.Response, 404, "RYNAT link not found", "链接不存在或格式不正确。");
            return;
        }

        var code = path[3..];
        if (!_shareLinkService.TryDecode(code, out var payload))
        {
            await WriteHtmlAsync(context.Response, 400, "RYNAT link invalid", "链接解析失败。");
            return;
        }

        try
        {
            _state.LastActivation = $"链接唤醒: {payload.Path}";
            await WriteHtmlAsync(context.Response, 200, "RYNAT activated", "正在打开资源管理器。", close: true);
            _ = Task.Run(async () =>
            {
                await Task.Delay(280);
                try
                {
                    _explorerService.OpenTarget(payload.Path, payload.Kind);
                }
                catch (Exception ex)
                {
                    _state.LastActivation = $"链接唤醒失败: {ex.Message}";
                }
            });
        }
        catch (Exception ex)
        {
            await WriteHtmlAsync(context.Response, 500, "RYNAT activation failed", ex.Message);
        }
    }

    private static async Task WriteHtmlAsync(HttpListenerResponse response, int statusCode, string title, string body, bool close = false)
    {
        response.StatusCode = statusCode;
        response.ContentType = "text/html; charset=utf-8";
        var closeScript = close ? "<script>setTimeout(() => window.close(), 120);</script>" : string.Empty;
        var html = $$"""
            <!doctype html>
            <html lang="zh-CN">
            <meta charset="utf-8">
            <title>{{WebUtility.HtmlEncode(title)}}</title>
            <body style="font-family:Segoe UI,Microsoft YaHei UI,sans-serif;margin:32px;color:#20242a">
              <h1 style="font-size:18px;margin:0 0 10px">{{WebUtility.HtmlEncode(title)}}</h1>
              <p style="font-size:14px;margin:0;color:#5f6670">{{WebUtility.HtmlEncode(body)}}</p>
              {{closeScript}}
            </body>
            </html>
            """;
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }
}
