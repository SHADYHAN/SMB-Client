using System.Text.Json;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Rynat.WindowsTray.App;

namespace Rynat.WindowsTray.UI;

internal sealed class ShellWindow : Form
{
    private readonly TrayApplicationContext _context;
    private readonly WebView2 _webView = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public ShellWindow(TrayApplicationContext context)
    {
        _context = context;

        Text = "RYNAT";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 640);
        Size = new Size(980, 700);
        BackColor = Color.FromArgb(246, 247, 249);
        Icon = LoadAppIcon();

        _webView.Dock = DockStyle.Fill;
        Controls.Add(_webView);

        FormClosing += OnFormClosing;
        Load += async (_, _) => await InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        await _webView.EnsureCoreWebView2Async();
        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _webView.CoreWebView2.Navigate(GetIndexPath());
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        WebCommand? message = null;
        try
        {
            message = JsonSerializer.Deserialize<WebCommand>(e.WebMessageAsJson, _jsonOptions);
            if (message is null)
            {
                return;
            }

            object? payload = message.Command switch
            {
                "getState" => _context.GetState(),
                "connect" => await _context.ConnectAsync(
                    message.ServerHost ?? string.Empty,
                    message.Username ?? string.Empty,
                    message.Password ?? string.Empty,
                    message.RememberPassword),
                "disconnect" => _context.Disconnect(),
                "openExplorer" => await RunAndReturnStateAsync(_context.OpenExplorerAsync),
                "copyTestLink" => new { link = await _context.CopyTestLinkAsync(), state = _context.GetState() },
                "copyMaterialTestLink" => new { link = await _context.CopyMaterialTestLinkAsync(), state = _context.GetState() },
                "hideWindow" => HideAndReturnState(),
                _ => new { error = $"unknown command: {message.Command}", state = _context.GetState() }
            };

            PostMessage(message.Id, payload);
        }
        catch (Exception ex)
        {
            PostMessage(message?.Id, new { error = ex.Message, state = _context.GetState() });
        }
    }

    private ShellState HideAndReturnState()
    {
        _context.HideWindow();
        return _context.GetState();
    }

    private async Task<ShellState> RunAndReturnStateAsync(Func<Task> action)
    {
        await action();
        return _context.GetState();
    }

    private void PostMessage(string? id, object? payload)
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(new { id, payload }, _jsonOptions);
        _webView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private static string GetIndexPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "UI", "WebAssets", "index.html");
        return new Uri(path).AbsoluteUri;
    }

    private static Icon LoadAppIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "RynatApp.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
    }

    private sealed class WebCommand
    {
        public string? Id { get; set; }

        public string Command { get; set; } = string.Empty;

        public string? ServerHost { get; set; }

        public string? Username { get; set; }

        public string? Password { get; set; }

        public bool RememberPassword { get; set; } = true;
    }
}
