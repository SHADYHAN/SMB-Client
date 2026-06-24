using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Rynat.WindowsClient.Platform.Activation;

public sealed class WindowsProtocolRegistrationService : IProtocolRegistrationService
{
    private const string Protocol = "rynat";
    private readonly string _executablePath;

    public WindowsProtocolRegistrationService()
        : this(Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty)
    {
    }

    public WindowsProtocolRegistrationService(string executablePath)
    {
        _executablePath = executablePath;
    }

    public void EnsureRegistered()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_executablePath) || !File.Exists(_executablePath))
            {
                return;
            }

            using var protocolKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Protocol}");
            if (protocolKey is null)
            {
                return;
            }

            protocolKey.SetValue(null, "URL:RYNAT Link");
            protocolKey.SetValue("URL Protocol", string.Empty);

            using var iconKey = protocolKey.CreateSubKey("DefaultIcon");
            iconKey?.SetValue(null, $"\"{_executablePath}\",0");

            using var commandKey = protocolKey.CreateSubKey(@"shell\open\command");
            commandKey?.SetValue(null, $"\"{_executablePath}\" \"%1\"");
        }
        catch
        {
            // Protocol registration should never block normal client startup.
        }
    }
}
