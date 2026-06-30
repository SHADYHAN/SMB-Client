using System.Text;
using System.Text.Json;
using Rynat.WindowsTray.App;

namespace Rynat.WindowsTray.Services;

internal sealed class ShellSettingsService
{
    private const string AppFolderName = "RYNAT";
    private const string SettingsFileName = "windows-tray-settings.json";
    private static readonly byte[] Entropy = "RYNAT.WindowsTray.Settings.v1"u8.ToArray();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public ShellSettings Load()
    {
        var path = SettingsPath;
        if (!File.Exists(path))
        {
            return CreateDefaultSettings();
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<ShellSettings>(json, _jsonOptions) ?? CreateDefaultSettings();
            Normalize(settings);
            return settings;
        }
        catch
        {
            return CreateDefaultSettings();
        }
    }

    public void Save(ShellSettings settings)
    {
        Normalize(settings);
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(SettingsPath, json, Encoding.UTF8);
    }

    public string? ProtectPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return null;
        }

        if (!OperatingSystem.IsWindows())
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(password));
        }

        var bytes = WindowsDataProtectionService.Protect(Encoding.UTF8.GetBytes(password), Entropy);
        return Convert.ToBase64String(bytes);
    }

    public string? UnprotectPassword(string? protectedPassword)
    {
        if (string.IsNullOrWhiteSpace(protectedPassword))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(protectedPassword);
            if (!OperatingSystem.IsWindows())
            {
                return Encoding.UTF8.GetString(bytes);
            }

            var unprotected = WindowsDataProtectionService.Unprotect(bytes, Entropy);
            return Encoding.UTF8.GetString(unprotected);
        }
        catch
        {
            return null;
        }
    }

    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppFolderName);

    private static string SettingsPath => Path.Combine(SettingsDirectory, SettingsFileName);

    private static ShellSettings CreateDefaultSettings()
    {
        var server = new ServerProfile
        {
            Name = "默认服务器",
            Host = "192.168.102.136",
            RememberPassword = true
        };

        return new ShellSettings
        {
            DefaultServerId = server.Id,
            Servers = new List<ServerProfile> { server },
            General = new GeneralSettings()
        };
    }

    private static void Normalize(ShellSettings settings)
    {
        settings.Servers ??= new List<ServerProfile>();
        settings.General ??= new GeneralSettings();

        foreach (var server in settings.Servers)
        {
            if (string.IsNullOrWhiteSpace(server.Id))
            {
                server.Id = Guid.NewGuid().ToString("N");
            }

            server.Name = string.IsNullOrWhiteSpace(server.Name) ? "共享网盘" : server.Name.Trim();
            server.Host = string.IsNullOrWhiteSpace(server.Host) ? "192.168.102.136" : server.Host.Trim();
            server.Username = server.Username?.Trim() ?? string.Empty;

            if (server.AutoLogin)
            {
                server.RememberPassword = true;
            }
        }

        if (settings.Servers.Count == 0)
        {
            var fallback = new ServerProfile
            {
                Name = "默认服务器",
                Host = "192.168.102.136",
                RememberPassword = true
            };
            settings.Servers.Add(fallback);
            settings.DefaultServerId = fallback.Id;
        }

        if (string.IsNullOrWhiteSpace(settings.DefaultServerId)
            || settings.Servers.All(server => server.Id != settings.DefaultServerId))
        {
            settings.DefaultServerId = settings.Servers[0].Id;
        }
    }
}
