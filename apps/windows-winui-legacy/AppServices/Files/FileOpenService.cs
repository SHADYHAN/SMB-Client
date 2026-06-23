using System.IO;
using Rynat.Client;
using Rynat.WindowsClient.Infrastructure;
using Rynat.WindowsClient.PlatformIntegration.Files;
using Rynat.WindowsClient.UI.Main;

namespace Rynat.WindowsClient.AppServices.Files;

public sealed class FileOpenService
{
    private readonly RynatCoreBridge _bridge;
    private readonly IWindowsFileLauncher _fileLauncher;
    private readonly WindowsClientDiagnostics _diagnostics;

    public FileOpenService(
        RynatCoreBridge bridge,
        IWindowsFileLauncher fileLauncher,
        WindowsClientDiagnostics diagnostics
    )
    {
        _bridge = bridge;
        _fileLauncher = fileLauncher;
        _diagnostics = diagnostics;
    }

    public async Task<FileOpenResult> OpenAsync(
        WindowsServerSession session,
        DirectoryItemViewModel item,
        CancellationToken cancellationToken = default
    )
    {
        string? localPath = null;
        string? partialPath = null;
        if (item.IsDirectory)
        {
            return new FileOpenResult(false, "当前选中的是文件夹，请直接进入文件夹。", null, "open.directory");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            localPath = BuildLocalCachePath(session, item);
            partialPath = localPath + ".part";
            DeleteIfExists(partialPath);
            var cached = _bridge.SmbCacheFile(
                new SmbCacheFileRequest(
                    item.ShareName,
                    item.RemotePath,
                    partialPath,
                    null,
                    session.ConnectionId
                )
            );

            cancellationToken.ThrowIfCancellationRequested();
            ReplaceWithCompletedFile(cached.LocalPath, localPath);

            var launched = await _fileLauncher.LaunchAsync(localPath, cancellationToken);
            if (!launched)
            {
                return new FileOpenResult(
                    false,
                    $"已下载 {item.Name}，但无法使用系统默认程序打开。",
                    localPath,
                    "open.launch_failed"
                );
            }

            return new FileOpenResult(
                true,
                $"已打开文件：{item.Name}",
                localPath,
                null
            );
        }
        catch (OperationCanceledException)
        {
            if (!string.IsNullOrWhiteSpace(partialPath))
            {
                DeleteIfExists(partialPath);
            }
            return new FileOpenResult(
                false,
                $"已取消打开文件：{item.Name}",
                null,
                "open.cancelled"
            );
        }
        catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex) || ex is IOException or UnauthorizedAccessException)
        {
            if (!string.IsNullOrWhiteSpace(partialPath))
            {
                DeleteIfExists(partialPath);
            }
            _diagnostics.Error(ex, $"打开文件失败：{item.DisplayPath}");
            return new FileOpenResult(
                false,
                $"打开文件失败：{ex.Message}",
                null,
                BridgeExceptionClassifier.ErrorCodeFor(ex, "open.failed")
            );
        }
    }

    public async Task<FileOpenResult> OpenLocalAsync(
        string localPath,
        string displayName,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
        {
            return new FileOpenResult(
                false,
                "本地预览文件不可用，请稍后重试。",
                null,
                "open.local_missing"
            );
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var launched = await _fileLauncher.LaunchAsync(localPath, cancellationToken);
            if (!launched)
            {
                return new FileOpenResult(
                    false,
                    $"无法使用系统默认程序打开：{displayName}",
                    localPath,
                    "open.launch_failed"
                );
            }

            return new FileOpenResult(
                true,
                $"已打开：{displayName}",
                localPath,
                null
            );
        }
        catch (OperationCanceledException)
        {
            return new FileOpenResult(
                false,
                $"已取消打开：{displayName}",
                null,
                "open.cancelled"
            );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _diagnostics.Error(ex, $"打开本地预览文件失败：{localPath}");
            return new FileOpenResult(
                false,
                $"打开失败：{ex.Message}",
                localPath,
                "open.local_failed"
            );
        }
    }

    private static string BuildLocalCachePath(WindowsServerSession session, DirectoryItemViewModel item)
    {
        var openDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rynat",
            "OpenCache",
            session.Profile.Id
        );
        System.IO.Directory.CreateDirectory(openDirectory);

        var extension = Path.GetExtension(item.Name);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".bin";
        }

        var safeName = string.Concat(
            item.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)
        );

        return Path.Combine(
            openDirectory,
            $"{safeName}-{StableCacheKey.FromParts(session.Profile.Id, item.ShareName, item.RemotePath, item.DisplayPath)}{extension}"
        );
    }

    private static void ReplaceWithCompletedFile(string partialPath, string completedPath)
    {
        if (File.Exists(completedPath))
        {
            File.Delete(completedPath);
        }

        File.Move(partialPath, completedPath);
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
