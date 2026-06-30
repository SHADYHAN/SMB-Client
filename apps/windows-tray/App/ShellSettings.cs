namespace Rynat.WindowsTray.App;

internal sealed class ShellSettings
{
    public string? DefaultServerId { get; set; }

    public List<ServerProfile> Servers { get; set; } = new();

    public GeneralSettings General { get; set; } = new();
}

internal sealed class ServerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "共享网盘";

    public string Host { get; set; } = "192.168.102.136";

    public string Username { get; set; } = string.Empty;

    public string? ProtectedPassword { get; set; }

    public bool RememberPassword { get; set; } = true;

    public bool AutoLogin { get; set; }
}

internal sealed class GeneralSettings
{
    public bool StartWithWindows { get; set; } = true;

    public string CopyLinkHotkey { get; set; } = "未设置";
}
