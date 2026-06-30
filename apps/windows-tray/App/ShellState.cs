namespace Rynat.WindowsTray.App;

internal sealed class ShellState
{
    public string ServerHost { get; set; } = "192.168.102.136";

    public string ServerName { get; set; } = "默认服务器";

    public string? DefaultServerId { get; set; }

    public List<ServerProfile> Servers { get; set; } = new();

    public GeneralSettings General { get; set; } = new();

    public string Username { get; set; } = string.Empty;

    public bool RememberPassword { get; set; } = true;

    public bool AutoLogin { get; set; }

    public bool HasStoredPassword { get; set; }

    public bool Connected { get; set; }

    public string Status { get; set; } = "请登录以打开共享网盘。";

    public string SmbSessionStatus => Connected
        ? $"已用 {Username} 连接共享网盘会话"
        : "尚未连接共享网盘会话";

    public bool LocalRedirectRunning { get; set; }

    public string LocalRedirectStatus { get; set; } = "本地短链服务尚未启动";

    public bool ContextIpcRunning { get; set; }

    public string ContextIpcStatus { get; set; } = "右键 IPC 服务尚未启动";

    public string LastActivation { get; set; } = "暂无";

    public string ExplorerRoot => string.IsNullOrWhiteSpace(ServerHost) ? string.Empty : $@"\\{ServerHost.Trim()}";
}
