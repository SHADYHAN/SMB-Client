using System.Drawing;
using System.Windows.Forms;
using Rynat.WindowsTray.Services;
using Rynat.WindowsTray.UI;

namespace Rynat.WindowsTray.App;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly ShellState _state = new();
    private readonly ExplorerService _explorerService = new();
    private readonly WindowsSmbSessionService _smbSessionService = new();
    private readonly ShareLinkService _shareLinkService = new();
    private readonly LocalRedirectService _localRedirectService;
    private readonly ContextIpcService _contextIpcService;
    private readonly NotifyIcon _notifyIcon;
    private ShellWindow? _window;

    public TrayApplicationContext(string[] args)
    {
        _localRedirectService = new LocalRedirectService(_state, _explorerService);
        _contextIpcService = new ContextIpcService(_state, _shareLinkService);

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "RYNAT",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();

        StartServices();
        ShowWindow();

        if (args.Length > 0)
        {
            _state.LastActivation = string.Join(" ", args);
        }
    }

    public ShellState GetState() => _state;

    public async Task<ShellState> ConnectAsync(string serverHost, string username, string password, bool rememberPassword)
    {
        var normalizedHost = string.IsNullOrWhiteSpace(serverHost)
            ? "192.168.102.136"
            : WindowsSmbSessionService.NormalizeHost(serverHost);
        var normalizedUsername = username.Trim();

        _state.Status = "正在用输入的账号连接 Windows SMB 会话。";
        await _smbSessionService.ConnectAsync(normalizedHost, normalizedUsername, password);

        _state.ServerHost = normalizedHost;
        _state.Username = normalizedUsername;
        _state.RememberPassword = rememberPassword;
        _state.Connected = true;
        _state.Status = "已登录 Windows SMB 会话，正在打开 Windows Explorer。";

        await OpenExplorerAsync();
        return _state;
    }

    public ShellState Disconnect()
    {
        _smbSessionService.DisconnectCurrent();
        _state.Connected = false;
        _state.Username = string.Empty;
        _state.Status = "已退出登录。";
        return _state;
    }

    public Task OpenExplorerAsync()
    {
        _explorerService.OpenDirectory(_state.ExplorerRoot);
        _state.LastActivation = $"打开 Explorer: {_state.ExplorerRoot}";
        return Task.CompletedTask;
    }

    public async Task<string> CopyTestLinkAsync()
    {
        var path = $@"\\{_state.ServerHost.Trim().Trim('\\')}\临时文件夹\123";
        var link = _shareLinkService.CreateShareLink(path, "dir");
        await ClipboardService.SetTextAsync(link);
        _state.LastActivation = $"已复制测试链接: {link}";
        return link;
    }

    public void HideWindow()
    {
        _window?.Hide();
    }

    public void ShowWindow()
    {
        if (_window is null || _window.IsDisposed)
        {
            _window = new ShellWindow(this);
        }

        _window.Show();
        _window.WindowState = FormWindowState.Normal;
        _window.Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _smbSessionService.DisconnectCurrent();
            _localRedirectService.Dispose();
            _contextIpcService.Dispose();
            _window?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void StartServices()
    {
        _localRedirectService.Start();
        _contextIpcService.Start();
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 RYNAT", null, (_, _) => ShowWindow());
        menu.Items.Add("打开资源管理器", null, async (_, _) => await OpenExplorerAsync());
        menu.Items.Add("复制测试链接", null, async (_, _) => await CopyTestLinkAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        return menu;
    }

    private static Icon LoadAppIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "RynatApp.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
    }
}
