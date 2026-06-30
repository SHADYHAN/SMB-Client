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
            await WriteHtmlAsync(context.Response, 404, "链接不可用", "链接不存在或格式不正确。");
            return;
        }

        var code = path[3..];
        if (!_shareLinkService.TryDecode(code, out var payload))
        {
            await WriteHtmlAsync(context.Response, 400, "链接解析失败", "请确认复制的是有效的共享网盘链接。");
            return;
        }

        try
        {
            if (!_state.Connected)
            {
                await WriteHtmlAsync(context.Response, 401, "请先登录", "请先打开并登录 RYANT共享网盘，然后重新点击链接。");
                return;
            }

            if (!IsPathUnderCurrentServer(payload.Path))
            {
                await WriteHtmlAsync(context.Response, 403, "无法打开链接", "该链接不属于当前登录的服务器。");
                return;
            }

            _state.LastActivation = $"链接唤醒: {payload.Path}";
            await WriteHtmlAsync(context.Response, 200, "正在打开共享网盘", "正在打开 Windows 资源管理器，标签页会自动关闭。", close: true);
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
            await WriteHtmlAsync(context.Response, 500, "打开失败", ex.Message);
        }
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

    private static async Task WriteHtmlAsync(HttpListenerResponse response, int statusCode, string title, string body, bool close = false)
    {
        response.StatusCode = statusCode;
        response.ContentType = "text/html; charset=utf-8";
        var safeTitle = WebUtility.HtmlEncode(title);
        var safeBody = WebUtility.HtmlEncode(body);
        var stateClass = statusCode >= 400 ? "error" : "ok";
        var actionText = close ? "如果没有自动关闭，可以直接关闭此标签页。" : "请返回客户端检查链接或登录状态。";
        var closeScript = close
            ? """
              <script>
                (() => {
                  const closeTab = () => {
                    try {
                      window.open("", "_self");
                      window.close();
                    } catch (_) {}
                  };
                  setTimeout(closeTab, 90);
                  setTimeout(closeTab, 320);
                  setTimeout(closeTab, 900);
                })();
              </script>
              """
            : string.Empty;
        var html = $$"""
            <!doctype html>
            <html lang="zh-CN">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{safeTitle}}</title>
              <style>
                :root {
                  color-scheme: light;
                  font-family: "Segoe UI", "Microsoft YaHei UI", system-ui, sans-serif;
                  background: #f4f7fb;
                  color: #17202c;
                }
                * { box-sizing: border-box; }
                body {
                  min-height: 100vh;
                  margin: 0;
                  display: grid;
                  place-items: center;
                  padding: 32px;
                  background: #f4f7fb;
                }
                main {
                  width: min(380px, 100%);
                  padding: 28px;
                  border: 1px solid rgba(255, 255, 255, 0.78);
                  border-radius: 12px;
                  background: rgba(255, 255, 255, 0.82);
                  box-shadow: 0 20px 52px rgba(30, 48, 72, 0.13);
                }
                .brand {
                  margin: 0 0 18px;
                  color: #2667c9;
                  font-size: 12px;
                  font-weight: 700;
                  letter-spacing: 0;
                }
                .status {
                  display: inline-grid;
                  place-items: center;
                  width: 34px;
                  height: 34px;
                  margin-bottom: 16px;
                  border-radius: 50%;
                  background: #edf4ff;
                  color: #2667c9;
                  font-weight: 800;
                }
                .status.ok::before { content: ""; width: 9px; height: 9px; border-radius: 50%; background: currentColor; box-shadow: 0 0 0 7px rgba(38, 103, 201, 0.12); }
                .status.error { background: #fff1f1; color: #ad2f2f; }
                .status.error::before { content: "!"; }
                h1 {
                  margin: 0 0 8px;
                  font-size: 20px;
                  line-height: 1.3;
                  font-weight: 700;
                }
                p {
                  margin: 0;
                  color: #657181;
                  font-size: 14px;
                  line-height: 1.7;
                }
                .note {
                  margin-top: 18px;
                  color: #95a0ad;
                  font-size: 12px;
                }
              </style>
            </head>
            <body>
              <main>
                <p class="brand">RYANT共享网盘</p>
                <div class="status {{stateClass}}" aria-hidden="true"></div>
                <h1>{{safeTitle}}</h1>
                <p>{{safeBody}}</p>
                <p class="note">{{WebUtility.HtmlEncode(actionText)}}</p>
              </main>
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
