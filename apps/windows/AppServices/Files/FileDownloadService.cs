using System.IO;
using Rynat.Client;
using Rynat.WindowsClient.AppServices.Tasks;
using Rynat.WindowsClient.Infrastructure;
using Rynat.WindowsClient.UI.Main;

namespace Rynat.WindowsClient.AppServices.Files;

public sealed class FileDownloadService
{
    private readonly RynatCoreBridge _bridge;
    private readonly WindowsClientDiagnostics _diagnostics;

    public FileDownloadService(
        RynatCoreBridge bridge,
        WindowsClientDiagnostics diagnostics
    )
    {
        _bridge = bridge;
        _diagnostics = diagnostics;
    }

    public Task<FileDownloadResult> DownloadAsync(
        WindowsServerSession session,
        DirectoryItemViewModel item,
        string localPath,
        WindowsFileTaskHandle? task = null,
        CancellationToken cancellationToken = default,
        bool updateTaskState = true
    )
    {
        return Task.Run(
            () => Download(session, item, localPath, task, cancellationToken, updateTaskState),
            cancellationToken
        );
    }

    public FileDownloadResult Download(
        WindowsServerSession session,
        DirectoryItemViewModel item,
        string localPath,
        WindowsFileTaskHandle? task = null,
        CancellationToken cancellationToken = default,
        bool updateTaskState = true
    )
    {
        var progressTask = updateTaskState ? task : null;
        if (item.IsDirectory)
        {
            return DownloadDirectory(session, item, localPath, task, cancellationToken, updateTaskState);
        }

        try
        {
            progressTask?.Start($"正在下载文件：{item.Name}...");
            cancellationToken.ThrowIfCancellationRequested();
            task?.CancellationToken.ThrowIfCancellationRequested();
            EnsureParentDirectory(localPath);
            var partialPath = BuildPartialPath(localPath);
            DeleteIfExists(partialPath);

            var cached = _bridge.SmbCacheFile(
                new SmbCacheFileRequest(
                    item.ShareName,
                    item.RemotePath,
                    partialPath,
                    null,
                    session.ConnectionId,
                    task?.CoreOperationId
                )
            );
            cancellationToken.ThrowIfCancellationRequested();
            task?.CancellationToken.ThrowIfCancellationRequested();
            ReplaceWithCompletedFile(cached.LocalPath, localPath);

            var result = new FileDownloadResult(
                true,
                $"已下载到本地：{localPath}",
                localPath,
                DownloadedFiles: 1
            );
            progressTask?.ReportProgress(1, 1, $"已下载：{item.Name}");
            progressTask?.Complete(result.Summary);
            return result;
        }
        catch (OperationCanceledException)
        {
            CleanupPartialFile(localPath);
            progressTask?.Cancel($"已取消下载：{item.Name}");
            return new FileDownloadResult(
                false,
                $"已取消下载：{item.Name}",
                null,
                ErrorCode: "download.cancelled"
            );
        }
        catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex) || ex is IOException or UnauthorizedAccessException)
        {
            CleanupPartialFile(localPath);
            _diagnostics.Error(ex, $"下载文件失败：{item.DisplayPath}");
            var result = new FileDownloadResult(
                false,
                $"下载文件失败：{ex.Message}",
                null,
                ErrorCode: BridgeExceptionClassifier.ErrorCodeFor(ex, "download.failed")
            );
            progressTask?.Fail(result.Summary, result.ErrorCode);
            return result;
        }
    }

    public Task<FileDownloadResult> PrepareDragDownloadAsync(
        WindowsServerSession session,
        DirectoryItemViewModel item,
        WindowsFileTaskHandle? task = null,
        CancellationToken cancellationToken = default,
        bool updateTaskState = true
    )
    {
        var localPath = BuildDragCachePath(session, item);
        var cached = TryReuseDragCache(item, localPath);
        if (cached is not null)
        {
            var progressTask = updateTaskState ? task : null;
            progressTask?.Start($"复用拖出缓存：{item.Name}");
            progressTask?.ReportProgress(1, 1, $"已复用拖出缓存：{item.Name}");
            progressTask?.Complete(cached.Summary);
            return Task.FromResult(cached);
        }

        return DownloadAsync(session, item, localPath, task, cancellationToken, updateTaskState);
    }

    public FileDownloadResult PrepareDragDownload(
        WindowsServerSession session,
        DirectoryItemViewModel item,
        WindowsFileTaskHandle? task = null,
        CancellationToken cancellationToken = default,
        bool updateTaskState = true
    )
    {
        var progressTask = updateTaskState ? task : null;
        var localPath = BuildDragCachePath(session, item);
        var cached = TryReuseDragCache(item, localPath);
        if (cached is not null)
        {
            progressTask?.Start($"复用拖出缓存：{item.Name}");
            progressTask?.ReportProgress(1, 1, $"已复用拖出缓存：{item.Name}");
            progressTask?.Complete(cached.Summary);
            return cached;
        }

        return Download(session, item, localPath, task, cancellationToken, updateTaskState);
    }

    public Task<FileDownloadResult> DownloadDirectoryAsync(
        WindowsServerSession session,
        DirectoryItemViewModel item,
        string localParentDirectory,
        WindowsFileTaskHandle? task = null,
        CancellationToken cancellationToken = default,
        bool updateTaskState = true
    )
    {
        return Task.Run(
            () => DownloadDirectory(session, item, localParentDirectory, task, cancellationToken, updateTaskState),
            cancellationToken
        );
    }

    public FileDownloadResult DownloadDirectory(
        WindowsServerSession session,
        DirectoryItemViewModel item,
        string localParentDirectory,
        WindowsFileTaskHandle? task = null,
        CancellationToken cancellationToken = default,
        bool updateTaskState = true
    )
    {
        var progressTask = updateTaskState ? task : null;
        if (!item.IsDirectory)
        {
            var targetPath = Path.Combine(localParentDirectory, SafeFileName(item.Name));
            return Download(session, item, targetPath, task, cancellationToken, updateTaskState);
        }

        try
        {
            progressTask?.Start($"正在下载文件夹：{item.Name}...");
            cancellationToken.ThrowIfCancellationRequested();
            task?.CancellationToken.ThrowIfCancellationRequested();
            var rootDirectory = Path.Combine(localParentDirectory, SafeFileName(item.Name));
            var partialRootDirectory = BuildPartialPath(rootDirectory);
            DeleteDirectoryIfExists(partialRootDirectory);
            System.IO.Directory.CreateDirectory(partialRootDirectory);

            var stats = new DirectoryDownloadStats(createdDirectories: 1);
            DownloadDirectoryChildren(
                session,
                item.ShareName,
                item.RemotePath,
                partialRootDirectory,
                stats,
                task,
                updateTaskState,
                cancellationToken
            );
            ReplaceWithCompletedDirectory(partialRootDirectory, rootDirectory);

            var result = new FileDownloadResult(
                true,
                $"文件夹已下载：{rootDirectory}，文件 {stats.DownloadedFiles} 个，文件夹 {stats.CreatedDirectories} 个。",
                rootDirectory,
                DownloadedFiles: stats.DownloadedFiles,
                CreatedDirectories: stats.CreatedDirectories,
                SkippedItems: stats.SkippedItems
            );
            progressTask?.Complete(result.Summary);
            return result;
        }
        catch (OperationCanceledException)
        {
            CleanupPartialDirectory(localParentDirectory, item);
            progressTask?.Cancel($"已取消下载文件夹：{item.Name}");
            return new FileDownloadResult(
                false,
                $"已取消下载文件夹：{item.Name}",
                null,
                ErrorCode: "download.cancelled"
            );
        }
        catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex) || ex is IOException or UnauthorizedAccessException)
        {
            CleanupPartialDirectory(localParentDirectory, item);
            _diagnostics.Error(ex, $"下载文件夹失败：{item.DisplayPath}");
            var result = new FileDownloadResult(
                false,
                $"下载文件夹失败：{ex.Message}",
                null,
                ErrorCode: BridgeExceptionClassifier.ErrorCodeFor(ex, "download.failed")
            );
            progressTask?.Fail(result.Summary, result.ErrorCode);
            return result;
        }
    }

    public Task<FileDownloadResult> PrepareDragDownloadDirectoryAsync(
        WindowsServerSession session,
        DirectoryItemViewModel item,
        WindowsFileTaskHandle? task = null,
        CancellationToken cancellationToken = default,
        bool updateTaskState = true
    )
    {
        var parentDirectory = BuildDragDirectoryCacheParent(session);
        return DownloadDirectoryAsync(session, item, parentDirectory, task, cancellationToken, updateTaskState);
    }

    public FileDownloadResult PrepareDragDownloadDirectory(
        WindowsServerSession session,
        DirectoryItemViewModel item,
        WindowsFileTaskHandle? task = null,
        CancellationToken cancellationToken = default,
        bool updateTaskState = true
    )
    {
        var parentDirectory = BuildDragDirectoryCacheParent(session);
        return DownloadDirectory(session, item, parentDirectory, task, cancellationToken, updateTaskState);
    }

    private void DownloadDirectoryChildren(
        WindowsServerSession session,
        string shareName,
        string remoteDirectoryPath,
        string localDirectoryPath,
        DirectoryDownloadStats stats,
        WindowsFileTaskHandle? task,
        bool updateTaskState,
        CancellationToken cancellationToken
    )
    {
        var progressTask = updateTaskState ? task : null;
        cancellationToken.ThrowIfCancellationRequested();
        task?.CancellationToken.ThrowIfCancellationRequested();

        var children = _bridge.SmbListDirectory(
            new SmbListDirectoryRequest(
                shareName,
                remoteDirectoryPath,
                session.ConnectionId,
                task?.CoreOperationId
            )
        );

        foreach (var child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            task?.CancellationToken.ThrowIfCancellationRequested();
            var localPath = Path.Combine(localDirectoryPath, SafeFileName(child.Name));
            if (child.IsDir)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(localPath);
                    stats.CreatedDirectories++;
                    DownloadDirectoryChildren(
                        session,
                        shareName,
                        child.Path,
                        localPath,
                        stats,
                        task,
                        updateTaskState,
                        cancellationToken
                    );
                }
                catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex) || ex is IOException or UnauthorizedAccessException)
                {
                    stats.SkippedItems++;
                    _diagnostics.Error(ex, $"下载目录中的子目录失败，已跳过：{child.Path}");
                }
                continue;
            }

            try
            {
                var partialPath = BuildPartialPath(localPath);
                DeleteIfExists(partialPath);
                _bridge.SmbCacheFile(
                    new SmbCacheFileRequest(
                        shareName,
                        child.Path,
                        partialPath,
                        null,
                        session.ConnectionId,
                        task?.CoreOperationId
                    )
                );
                ReplaceWithCompletedFile(partialPath, localPath);
                stats.DownloadedFiles++;
                progressTask?.ReportProgress(
                    stats.DownloadedFiles,
                    null,
                    $"正在下载文件夹：已下载 {stats.DownloadedFiles} 个文件。"
                );
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex) || ex is IOException or UnauthorizedAccessException)
            {
                CleanupPartialFile(localPath);
                stats.SkippedItems++;
                _diagnostics.Error(ex, $"下载目录中的文件失败，已跳过：{child.Path}");
            }
        }
    }

    private static FileDownloadResult? TryReuseDragCache(
        DirectoryItemViewModel item,
        string localPath
    )
    {
        if (!File.Exists(localPath))
        {
            return null;
        }

        var info = new FileInfo(localPath);
        if (info.Length <= 0)
        {
            return null;
        }

        if (!item.SizeBytes.HasValue)
        {
            return null;
        }

        if (info.Length != unchecked((long)item.SizeBytes.Value))
        {
            return null;
        }

        return new FileDownloadResult(
            true,
            $"已复用本地拖出缓存：{item.Name}",
            localPath,
            DownloadedFiles: 1
        );
    }

    private static void EnsureParentDirectory(string localPath)
    {
        var directory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }
    }

    private static string BuildDragCachePath(WindowsServerSession session, DirectoryItemViewModel item)
    {
        var dragDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rynat",
            "DragDownloadCache",
            session.Profile.Id
        );
        System.IO.Directory.CreateDirectory(dragDirectory);

        var safeName = SafeFileName(item.Name);

        return Path.Combine(
            dragDirectory,
            $"{Path.GetFileNameWithoutExtension(safeName)}-{StableCacheKey.FromParts(session.Profile.Id, item.ShareName, item.RemotePath, item.DisplayPath)}{Path.GetExtension(safeName)}"
        );
    }

    private static string BuildDragDirectoryCacheParent(WindowsServerSession session)
    {
        var dragDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rynat",
            "DragDownloadCache",
            session.Profile.Id
        );
        System.IO.Directory.CreateDirectory(dragDirectory);
        return dragDirectory;
    }

    private static string SafeFileName(string name)
    {
        var safeName = string.Concat(
            name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)
        );
        if (string.IsNullOrWhiteSpace(safeName) || safeName == "." || safeName == "..")
        {
            return "download";
        }

        return safeName;
    }

    private static string BuildPartialPath(string completedPath) => completedPath + ".part";

    private static void ReplaceWithCompletedFile(string partialPath, string completedPath)
    {
        EnsureParentDirectory(completedPath);
        if (File.Exists(completedPath))
        {
            File.Delete(completedPath);
        }

        File.Move(partialPath, completedPath);
    }

    private static void CleanupPartialFile(string completedPath)
    {
        DeleteIfExists(BuildPartialPath(completedPath));
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

    private static void ReplaceWithCompletedDirectory(string partialPath, string completedPath)
    {
        DeleteDirectoryIfExists(completedPath);
        System.IO.Directory.Move(partialPath, completedPath);
    }

    private static void CleanupPartialDirectory(
        string localParentDirectory,
        DirectoryItemViewModel item
    )
    {
        var rootDirectory = Path.Combine(localParentDirectory, SafeFileName(item.Name));
        DeleteDirectoryIfExists(BuildPartialPath(rootDirectory));
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        try
        {
            if (System.IO.Directory.Exists(path))
            {
                System.IO.Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class DirectoryDownloadStats
    {
        public DirectoryDownloadStats(int createdDirectories = 0)
        {
            CreatedDirectories = createdDirectories;
        }

        public int DownloadedFiles { get; set; }

        public int CreatedDirectories { get; set; }

        public int SkippedItems { get; set; }
    }
}
