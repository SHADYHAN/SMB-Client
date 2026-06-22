using Microsoft.Win32;
using Rynat.WindowsClient.Infrastructure;
using System.Diagnostics;

namespace Rynat.WindowsClient.PlatformIntegration.Links;

public sealed class WindowsLinkPlatformRegistrationService
{
    private const string ProtocolKeyPath = @"Software\Classes\rynat";
    private const string HelperRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string HelperRunValueName = "RynatLinkRedirectHelper";

    private readonly WindowsClientDiagnostics _diagnostics;

    public WindowsLinkPlatformRegistrationService(WindowsClientDiagnostics diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public void EnsureProtocolRegistration(string executablePath)
    {
        try
        {
            using var protocolKey = Registry.CurrentUser.CreateSubKey(ProtocolKeyPath);
            protocolKey?.SetValue(string.Empty, "URL:RYNAT Link");
            protocolKey?.SetValue("URL Protocol", string.Empty);

            using var iconKey = protocolKey?.CreateSubKey("DefaultIcon");
            iconKey?.SetValue(string.Empty, $"\"{executablePath}\",0");

            using var commandKey = protocolKey?.CreateSubKey(@"shell\open\command");
            commandKey?.SetValue(string.Empty, $"\"{executablePath}\" --open-link \"%1\"");

            _diagnostics.Info("已更新 rynat:// 协议注册。");
        }
        catch (Exception ex)
        {
            _diagnostics.Error(ex, "更新 rynat:// 协议注册失败");
        }
    }

    public void EnsureRedirectHelperAutoStart(string executablePath)
    {
        try
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(HelperRunKeyPath);
            runKey?.SetValue(HelperRunValueName, $"\"{executablePath}\" --redirect-helper");
            _diagnostics.Info("已更新本地链接助手自启动配置。");
        }
        catch (Exception ex)
        {
            _diagnostics.Error(ex, "更新本地链接助手自启动配置失败");
        }
    }

    public void RemoveRedirectHelperAutoStart()
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(HelperRunKeyPath, writable: true);
            runKey?.DeleteValue(HelperRunValueName, throwOnMissingValue: false);
            _diagnostics.Info("已清理本地链接助手自启动配置。");
        }
        catch (Exception ex)
        {
            _diagnostics.Error(ex, "清理本地链接助手自启动配置失败");
        }
    }

    public void RemoveProtocolRegistration()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(ProtocolKeyPath, throwOnMissingSubKey: false);
            _diagnostics.Info("已清理 rynat:// 协议注册。");
        }
        catch (Exception ex)
        {
            _diagnostics.Error(ex, "清理 rynat:// 协议注册失败");
        }
    }

    public void StartRedirectHelper(string executablePath)
    {
        try
        {
            var startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("--redirect-helper");
            using var process = Process.Start(startInfo);
            _diagnostics.Info("已触发本地链接助手启动。");
        }
        catch (Exception ex)
        {
            _diagnostics.Error(ex, "启动本地链接助手失败");
        }
    }
}
