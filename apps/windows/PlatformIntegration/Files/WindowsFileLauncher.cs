using Windows.Storage;
using Windows.System;

namespace Rynat.WindowsClient.PlatformIntegration.Files;

public sealed class WindowsFileLauncher : IWindowsFileLauncher
{
    public async Task<bool> LaunchAsync(string localPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storageFile = await StorageFile.GetFileFromPathAsync(localPath);
        return await Launcher.LaunchFileAsync(storageFile);
    }
}
