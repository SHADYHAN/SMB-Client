namespace Rynat.WindowsTray.App;

internal sealed class ShellState
{
    public string ServerHost { get; set; } = "192.168.102.136";

    public string Username { get; set; } = string.Empty;

    public bool RememberPassword { get; set; } = true;

    public bool Connected { get; set; }

    public string Status { get; set; } = "请登录以打开 Windows Explorer。";

    public string SmbSessionStatus => Connected
        ? $"已用 {Username} 连接 Windows SMB 会话"
        : "尚未连接 Windows SMB 会话";

    public bool LocalRedirectRunning { get; set; }

    public string LocalRedirectStatus { get; set; } = "本地短链服务尚未启动";

    public bool ContextIpcRunning { get; set; }

    public string ContextIpcStatus { get; set; } = "右键 IPC 服务尚未启动";

    public string LastActivation { get; set; } = "暂无";

    public string ExplorerRoot => string.IsNullOrWhiteSpace(ServerHost) ? string.Empty : $@"\\{ServerHost.Trim()}";
}
