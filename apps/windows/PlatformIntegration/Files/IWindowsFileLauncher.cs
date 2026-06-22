namespace Rynat.WindowsClient.PlatformIntegration.Files;

public interface IWindowsFileLauncher
{
    Task<bool> LaunchAsync(string localPath, CancellationToken cancellationToken = default);
}
