using System.Drawing;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
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
    private readonly StartupRegistrationService _startupRegistrationService = new();
    private readonly GlobalHotkeyService _globalHotkeyService = new();
    private readonly LocalRedirectService _localRedirectService;
    private readonly ContextIpcService _contextIpcService;
    private readonly NotifyIcon _notifyIcon;
    private readonly string _activationPipeName;
    private readonly Control _uiDispatcher = new();
    private CancellationTokenSource? _activationPipeCts;
    private ShellSettings _settings;
    private ShellWindow? _window;
    private Task? _autoLoginTask;
    private readonly bool _autoLoginOpensExplorer;
    private bool _hotkeyRegistrationFailed;

    public TrayApplicationContext(string[] args, string activationPipeName, bool createdByStartup)
    {
        _activationPipeName = activationPipeName;
        _autoLoginOpensExplorer = !createdByStartup;
        _settings = _settingsService.Load();
        ApplySettingsToState();

        _localRedirectService = new LocalRedirectService(_state, _explorerService);
        _contextIpcService = new ContextIpcService(_state, _shareLinkService);

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "RYANT共享网盘",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
        _globalHotkeyService.Pressed += (_, _) => CopyForegroundExplorerLink();

        _uiDispatcher.CreateControl();
        _ = _uiDispatcher.Handle;
        StartServices();
        StartActivationPipe();
        _autoLoginTask = TryAutoLoginAsync(openExplorerAfterConnect: _autoLoginOpensExplorer);
        HandleActivationArgs(args, showWindowWhenIdle: !createdByStartup);

        if (args.Length > 0)
        {
            _state.LastActivation = string.Join(" ", args);
        }
    }

    public ShellState GetState()
    {
        ApplySettingsToState();
        return _state;
    }

    public async Task<ShellState> ConnectAsync(string username, string password, bool rememberPassword, bool autoLogin, bool openExplorerAfterConnect = true)
    {
        var server = GetDefaultServer();
        var normalizedHost = WindowsSmbSessionService.NormalizeHost(server.Host);
        var normalizedUsername = username.Trim();
        var resolvedPassword = ResolvePassword(server, normalizedUsername, password);

        _state.Status = "正在用输入的账号连接共享网盘。";
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
        _state.Status = openExplorerAfterConnect
            ? "已登录共享网盘，正在打开资源管理器。"
            : "已登录共享网盘。";

        if (openExplorerAfterConnect)
        {
            await OpenExplorerAsync();
        }

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
        _startupRegistrationService.SetEnabled(general.StartWithWindows);
        _settings.General.StartWithWindows = general.StartWithWindows;
        _settings.General.CopyLinkHotkeyEnabled = general.CopyLinkHotkeyEnabled;
        _settings.General.CopyLinkHotkey = "Ctrl + Shift + L";
        SaveSettings();
        ApplyHotkeySetting(updateStatus: true);
        NotifyStateChanged();
        return GetState();
    }

    public Task OpenExplorerAsync()
    {
        _explorerService.OpenDirectory(_state.ExplorerRoot);
        _state.LastActivation = $"打开资源管理器: {_state.ExplorerRoot}";
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
            _activationPipeCts?.Cancel();
            _activationPipeCts?.Dispose();
            _uiDispatcher.Dispose();
            _globalHotkeyService.Dispose();
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
        ApplyHotkeySetting(updateStatus: false);
    }

    private void ApplyHotkeySetting(bool updateStatus)
    {
        if (!_settings.General.CopyLinkHotkeyEnabled)
        {
            _globalHotkeyService.Unregister();
            _hotkeyRegistrationFailed = false;
            _state.General.CopyLinkHotkey = "Ctrl + Shift + L";
            _state.General.CopyLinkHotkeyEnabled = false;
            if (updateStatus)
            {
                _state.Status = "通用设置已保存，快捷键已禁用。";
            }

            return;
        }

        if (_globalHotkeyService.Register())
        {
            _hotkeyRegistrationFailed = false;
            _state.General.CopyLinkHotkey = "Ctrl + Shift + L";
            _state.General.CopyLinkHotkeyEnabled = true;
            if (updateStatus)
            {
                _state.Status = "通用设置已保存。";
            }
        }
        else
        {
            _hotkeyRegistrationFailed = true;
            _state.General.CopyLinkHotkey = "Ctrl + Shift + L 被占用";
            _state.General.CopyLinkHotkeyEnabled = true;
            if (updateStatus)
            {
                _state.Status = "快捷键 Ctrl + Shift + L 注册失败，可能已被其他软件占用。";
            }
        }
    }

    private void StartActivationPipe()
    {
        _activationPipeCts = new CancellationTokenSource();
        _ = Task.Run(() => RunActivationPipeAsync(_activationPipeCts.Token));
    }

    private async Task RunActivationPipeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _activationPipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var json = await reader.ReadToEndAsync(cancellationToken);
                var args = JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
                BeginInvokeOnUi(() => HandleActivationArgs(args, showWindowWhenIdle: true));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                await Task.Delay(250, cancellationToken);
            }
        }
    }

    private void BeginInvokeOnUi(Action action)
    {
        _uiDispatcher.BeginInvoke((MethodInvoker)(() => action()));
    }

    private void HandleActivationArgs(string[] args, bool showWindowWhenIdle)
    {
        if (args.Any(arg => string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (args.Any(arg => string.Equals(arg, "--open-share", StringComparison.OrdinalIgnoreCase)))
        {
            _ = OpenShareOrLoginAsync();
            return;
        }

        if (showWindowWhenIdle)
        {
            _ = OpenShareOrLoginAsync();
        }
    }

    private async Task OpenShareOrLoginAsync()
    {
        var waitingForAutoLogin = !_state.Connected && _autoLoginTask is { IsCompleted: false };
        if (!_state.Connected && _autoLoginTask is not null)
        {
            await _autoLoginTask;
        }

        if (waitingForAutoLogin && _state.Connected && _autoLoginOpensExplorer)
        {
            return;
        }

        if (_state.Connected)
        {
            await OpenExplorerAsync();
            return;
        }

        ShowWindow();
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 RYANT共享网盘", null, (_, _) => ShowWindow());
        menu.Items.Add("打开共享网盘", null, async (_, _) => await OpenShareOrLoginAsync());
        menu.Items.Add("退出登录", null, (_, _) => Disconnect());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        return menu;
    }

    private void CopyForegroundExplorerLink()
    {
        try
        {
            if (!_state.Connected)
            {
                ShowBalloonTip("请先登录后再复制分享链接。", ToolTipIcon.Warning);
                return;
            }

            if (!_explorerService.TryGetForegroundSelection(out var selection))
            {
                ShowBalloonTip("请在资源管理器中选中文件或文件夹。", ToolTipIcon.Warning);
                return;
            }

            if (!UncPathPolicy.IsPathUnderServer(selection.Path, _state.ServerHost))
            {
                ShowBalloonTip("只能复制当前登录服务器下的文件链接。", ToolTipIcon.Warning);
                return;
            }

            var link = _shareLinkService.CreateShareLink(selection.Path, selection.Kind);
            ClipboardService.SetTextAsync(link).GetAwaiter().GetResult();
            _state.LastActivation = $"快捷键复制链接: {selection.Path}";
            _state.Status = "分享链接已复制。";
            NotifyStateChanged();
        }
        catch (Exception ex)
        {
            _state.Status = ex.Message;
            NotifyStateChanged();
            ShowBalloonTip(ex.Message, ToolTipIcon.Warning);
        }
    }

    private void ShowBalloonTip(string message, ToolTipIcon icon)
    {
        _notifyIcon.BalloonTipTitle = "RYANT共享网盘";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(2600);
    }

    private static Icon LoadAppIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "RynatApp.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
    }

    private async Task TryAutoLoginAsync(bool openExplorerAfterConnect)
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
            await ConnectAsync(server.Username, string.Empty, rememberPassword: true, autoLogin: true, openExplorerAfterConnect);
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
            StartWithWindows = _startupRegistrationService.IsEnabled(),
            CopyLinkHotkeyEnabled = _settings.General.CopyLinkHotkeyEnabled,
            CopyLinkHotkey = _hotkeyRegistrationFailed
                ? "Ctrl + Shift + L 被占用"
                : "Ctrl + Shift + L"
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
