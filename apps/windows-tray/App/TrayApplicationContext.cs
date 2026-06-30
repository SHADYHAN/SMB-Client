using System.Drawing;
using System.Windows.Forms;
using Rynat.WindowsTray.Services;
using Rynat.WindowsTray.UI;

namespace Rynat.WindowsTray.App;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly ShellState _state = new();
    private readonly ShellSettingsService _settingsService = new();
    private readonly ExplorerService _explorerService = new();
    private readonly WindowsSmbSessionService _smbSessionService = new();
    private readonly ShareLinkService _shareLinkService = new();
    private readonly LocalRedirectService _localRedirectService;
    private readonly ContextIpcService _contextIpcService;
    private readonly NotifyIcon _notifyIcon;
    private ShellSettings _settings;
    private ShellWindow? _window;

    public TrayApplicationContext(string[] args)
    {
        _settings = _settingsService.Load();
        ApplySettingsToState();

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

        _ = TryAutoLoginAsync();
    }

    public ShellState GetState()
    {
        ApplySettingsToState();
        return _state;
    }

    public async Task<ShellState> ConnectAsync(string username, string password, bool rememberPassword, bool autoLogin)
    {
        var server = GetDefaultServer();
        var normalizedHost = WindowsSmbSessionService.NormalizeHost(server.Host);
        var normalizedUsername = username.Trim();
        var resolvedPassword = ResolvePassword(server, normalizedUsername, password);

        _state.Status = "正在用输入的账号连接 Windows SMB 会话。";
        await _smbSessionService.ConnectAsync(normalizedHost, normalizedUsername, resolvedPassword);

        server.Host = normalizedHost;
        server.Username = normalizedUsername;
        server.RememberPassword = rememberPassword || autoLogin;
        server.AutoLogin = autoLogin;
        server.ProtectedPassword = server.RememberPassword
            ? _settingsService.ProtectPassword(resolvedPassword)
            : null;

        _state.ServerHost = normalizedHost;
        _state.ServerName = server.Name;
        _state.Username = normalizedUsername;
        _state.RememberPassword = server.RememberPassword;
        _state.AutoLogin = server.AutoLogin;
        _state.HasStoredPassword = !string.IsNullOrWhiteSpace(server.ProtectedPassword);
        _state.Connected = true;
        SaveSettings();
        _state.Status = "已登录 Windows SMB 会话，正在打开 Windows Explorer。";

        await OpenExplorerAsync();
        NotifyStateChanged();
        return _state;
    }

    public ShellState Disconnect()
    {
        _smbSessionService.DisconnectCurrent();
        _state.Connected = false;
        _state.Username = string.Empty;
        _state.Status = "已退出登录。";
        ApplySettingsToState();
        NotifyStateChanged();
        return _state;
    }

    public SaveServerResult SaveServer(ServerProfile server, bool setDefault)
    {
        var normalized = NormalizeServerProfile(server);
        var existing = _settings.Servers.FirstOrDefault(item => item.Id == normalized.Id);
        if (existing is null)
        {
            _settings.Servers.Add(normalized);
        }
        else
        {
            var hostChanged = !string.Equals(existing.Host, normalized.Host, StringComparison.OrdinalIgnoreCase);
            existing.Name = normalized.Name;
            existing.Host = normalized.Host;
            if (hostChanged)
            {
                existing.Username = string.Empty;
                existing.ProtectedPassword = null;
                existing.RememberPassword = true;
                existing.AutoLogin = false;
            }
        }

        if (setDefault || string.IsNullOrWhiteSpace(_settings.DefaultServerId))
        {
            _settings.DefaultServerId = normalized.Id;
        }

        SaveSettings();
        _state.Status = "服务器设置已保存。";
        NotifyStateChanged();
        return new SaveServerResult
        {
            SavedServerId = normalized.Id,
            State = GetState()
        };
    }

    public ShellState DeleteServer(string id)
    {
        if (_settings.Servers.Count <= 1)
        {
            throw new InvalidOperationException("至少需要保留一个服务器。");
        }

        var removed = _settings.Servers.RemoveAll(server => server.Id == id);
        if (removed == 0)
        {
            throw new InvalidOperationException("找不到要删除的服务器。");
        }

        if (_settings.DefaultServerId == id)
        {
            _settings.DefaultServerId = _settings.Servers[0].Id;
        }

        SaveSettings();
        _state.Status = "服务器已删除。";
        NotifyStateChanged();
        return GetState();
    }

    public ShellState SetDefaultServer(string id)
    {
        if (_settings.Servers.All(server => server.Id != id))
        {
            throw new InvalidOperationException("找不到要设为默认的服务器。");
        }

        _settings.DefaultServerId = id;
        SaveSettings();
        _state.Status = "默认服务器已更新。";
        NotifyStateChanged();
        return GetState();
    }

    public ShellState SaveGeneralSettings(GeneralSettings general)
    {
        _settings.General.StartWithWindows = general.StartWithWindows;
        _settings.General.CopyLinkHotkey = string.IsNullOrWhiteSpace(general.CopyLinkHotkey)
            ? "未设置"
            : general.CopyLinkHotkey.Trim();
        SaveSettings();
        _state.Status = "通用设置已保存。";
        NotifyStateChanged();
        return GetState();
    }

    public Task OpenExplorerAsync()
    {
        _explorerService.OpenDirectory(_state.ExplorerRoot);
        _state.LastActivation = $"打开 Explorer: {_state.ExplorerRoot}";
        NotifyStateChanged();
        return Task.CompletedTask;
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
        menu.Items.Add("打开共享网盘", null, async (_, _) => await OpenExplorerAsync());
        menu.Items.Add("退出登录", null, (_, _) => Disconnect());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        return menu;
    }

    private static Icon LoadAppIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "RynatApp.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
    }

    private async Task TryAutoLoginAsync()
    {
        var server = GetDefaultServer();
        if (!server.AutoLogin
            || !server.RememberPassword
            || string.IsNullOrWhiteSpace(server.Username)
            || string.IsNullOrWhiteSpace(server.ProtectedPassword))
        {
            return;
        }

        try
        {
            _state.Status = $"正在自动登录 {server.Name}。";
            NotifyStateChanged();
            await ConnectAsync(server.Username, string.Empty, rememberPassword: true, autoLogin: true);
        }
        catch (Exception ex)
        {
            _state.Connected = false;
            _state.Status = $"自动登录失败：{ex.Message}";
            NotifyStateChanged();
        }
    }

    private void NotifyStateChanged()
    {
        _window?.PostStateUpdate();
    }

    private ServerProfile GetDefaultServer()
    {
        var server = _settings.Servers.FirstOrDefault(item => item.Id == _settings.DefaultServerId)
            ?? _settings.Servers.FirstOrDefault();
        return server ?? throw new InvalidOperationException("请先在服务器设置里添加默认服务器。");
    }

    private string ResolvePassword(ServerProfile server, string username, string password)
    {
        if (!string.IsNullOrEmpty(password))
        {
            return password;
        }

        if (!server.RememberPassword || !string.Equals(server.Username, username, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("请输入密码。");
        }

        return _settingsService.UnprotectPassword(server.ProtectedPassword)
            ?? throw new InvalidOperationException("已保存密码不可用，请重新输入。");
    }

    private void ApplySettingsToState()
    {
        var server = GetDefaultServer();
        _state.DefaultServerId = _settings.DefaultServerId;
        _state.Servers = _settings.Servers
            .Select(CloneServerForState)
            .ToList();
        _state.General = new GeneralSettings
        {
            StartWithWindows = _settings.General.StartWithWindows,
            CopyLinkHotkey = _settings.General.CopyLinkHotkey
        };

        if (!_state.Connected)
        {
            _state.ServerHost = server.Host;
            _state.ServerName = server.Name;
            _state.Username = server.Username;
            _state.RememberPassword = server.RememberPassword;
            _state.AutoLogin = server.AutoLogin;
            _state.HasStoredPassword = !string.IsNullOrWhiteSpace(server.ProtectedPassword);
        }
    }

    private void SaveSettings()
    {
        _settingsService.Save(_settings);
        _settings = _settingsService.Load();
        ApplySettingsToState();
    }

    private static ServerProfile NormalizeServerProfile(ServerProfile server)
    {
        return new ServerProfile
        {
            Id = string.IsNullOrWhiteSpace(server.Id) ? Guid.NewGuid().ToString("N") : server.Id,
            Name = string.IsNullOrWhiteSpace(server.Name) ? "共享网盘" : server.Name.Trim(),
            Host = WindowsSmbSessionService.NormalizeHost(server.Host),
            Username = server.Username?.Trim() ?? string.Empty,
            RememberPassword = server.RememberPassword,
            AutoLogin = server.AutoLogin,
            ProtectedPassword = server.ProtectedPassword
        };
    }

    private static ServerProfile CloneServerForState(ServerProfile server)
    {
        return new ServerProfile
        {
            Id = server.Id,
            Name = server.Name,
            Host = server.Host,
            Username = server.Username,
            RememberPassword = server.RememberPassword,
            AutoLogin = server.AutoLogin,
            ProtectedPassword = string.IsNullOrWhiteSpace(server.ProtectedPassword) ? null : "***"
        };
    }
}

internal sealed class SaveServerResult
{
    public string SavedServerId { get; set; } = string.Empty;

    public ShellState State { get; set; } = new();
}
