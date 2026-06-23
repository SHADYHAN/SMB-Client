using System.IO;
using Rynat.Client;
using Rynat.WindowsClient.AppServices.Tasks;
using Rynat.WindowsClient.Infrastructure;
using Rynat.WindowsClient.UI.Main;

namespace Rynat.WindowsClient.AppServices.Files;

public sealed class FileFolderUploadService
{
    private readonly RynatCoreBridge _bridge;
    private readonly WindowsClientDiagnostics _diagnostics;

    public FileFolderUploadService(
        RynatCoreBridge bridge,
        WindowsClientDiagnostics diagnostics
    )
    {
        _bridge = bridge;
        _diagnostics = diagnostics;
    }

    public Task<FileFolderUploadResult> UploadDirectoryAsync(
        WindowsServerSession session,
        string currentDisplayPath,
        string localDirectoryPath,
        IReadOnlyDictionary<string, UploadConflictDecision>? conflictDecisions = null,
        WindowsFileTaskHandle? task = null,
        CancellationToken cancellationToken = default,
        bool updateTaskState = true,
        bool manageTaskState = true
    )
    {
        return Task.Run(
            () => UploadDirectory(
                session,
                currentDisplayPath,
                localDirectoryPath,
                conflictDecisions,
                task,
                cancellationToken,
                updateTaskState,
                manageTaskState
            ),
            cancellationToken
        );
    }

    public FileFolderUploadResult UploadDirectory(
        WindowsServerSession session,
        string currentDisplayPath,
        string localDirectoryPath,
        IReadOnlyDictionary<string, UploadConflictDecision>? conflictDecisions = null,
        WindowsFileTaskHandle? task = null,
        CancellationToken cancellationToken = default,
        bool updateTaskState = true,
        bool manageTaskState = true
    )
    {
        var progressTask = updateTaskState ? task : null;
        if (!System.IO.Directory.Exists(localDirectoryPath))
        {
            return new FileFolderUploadResult(
                false,
                "本地文件夹不存在，无法上传。",
                0,
                0,
                0,
                0,
                "upload.directory_not_found"
            );
        }

        var location = session.ResolveLocation(currentDisplayPath);
        if (location is null)
        {
            return new FileFolderUploadResult(
                false,
                "请先进入一个共享目录后再上传文件夹。",
                0,
                0,
                0,
                0,
                "upload.invalid_location"
            );
        }

        try
        {
            if (manageTaskState)
            {
                progressTask?.Start($"正在上传文件夹：{Path.GetFileName(localDirectoryPath)}...");
            }
            cancellationToken.ThrowIfCancellationRequested();
            task?.CancellationToken.ThrowIfCancellationRequested();

            var stats = new FolderUploadStats();
            var rootInfo = new DirectoryInfo(localDirectoryPath);
            var remoteRootPath = AppendRemotePath(location.RemotePath, rootInfo.Name);
            var totalFiles = CountFilesSafe(rootInfo, cancellationToken);
            progressTask?.ReportProgress(0, totalFiles, $"正在准备上传：{rootInfo.Name}");

            var parentItems = _bridge.SmbListDirectory(
                new SmbListDirectoryRequest(
                    location.ShareName,
                    location.RemotePath,
                    session.ConnectionId,
                    task?.CoreOperationId
                )
            );
            var existingRoot = parentItems.FirstOrDefault(item =>
                string.Equals(item.Name, rootInfo.Name, StringComparison.OrdinalIgnoreCase)
            );
            if (existingRoot is not null)
            {
                var decision = ResolveDecision(conflictDecisions, localDirectoryPath, remoteRootPath);
                if (decision == UploadConflictDecision.Skip)
                {
                    var skippedResult = new FileFolderUploadResult(
                        false,
                        $"目标位置已存在同名项目，已跳过文件夹：{rootInfo.Name}",
                        0,
                        0,
                        0,
                        1,
                        "upload.skipped"
                    );
                    if (manageTaskState)
                    {
                        progressTask?.Complete(skippedResult.Summary);
                    }
                    return skippedResult;
                }

                if (!existingRoot.IsDir)
                {
                    DeleteRemoteItem(
                        session,
                        location.ShareName,
                        existingRoot,
                        task,
                        cancellationToken
                    );
                    stats.ReplacedItems++;
                }
            }

            UploadDirectoryRecursive(
                session,
                location.ShareName,
                localDirectoryPath,
                remoteRootPath,
                conflictDecisions,
                stats,
                task,
                totalFiles,
                updateTaskState,
                cancellationToken
            );

            var summary = BuildSummary(stats, rootInfo.Name);
            var result = new FileFolderUploadResult(
                (stats.UploadedFiles > 0 || stats.CreatedDirectories > 0) && stats.FailedFiles == 0,
                summary,
                stats.UploadedFiles,
                stats.CreatedDirectories,
                stats.ReplacedItems,
                stats.SkippedItems,
                FailedFiles: stats.FailedFiles,
                Errors: stats.Errors
            );

            if (manageTaskState)
            {
                progressTask?.Complete(result.Summary);
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            if (manageTaskState)
            {
                progressTask?.Cancel($"已取消上传文件夹：{Path.GetFileName(localDirectoryPath)}");
            }
            return new FileFolderUploadResult(
                false,
                $"已取消上传文件夹：{Path.GetFileName(localDirectoryPath)}",
                0,
                0,
                0,
                0,
                "upload.cancelled"
            );
        }
        catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex) || ex is IOException or UnauthorizedAccessException)
        {
            _diagnostics.Error(ex, $"上传文件夹失败：{localDirectoryPath}");
            var errorCode = BridgeExceptionClassifier.ErrorCodeFor(ex, "upload.failed");
            if (manageTaskState)
            {
                progressTask?.Fail($"上传文件夹失败：{ex.Message}", errorCode);
            }
            return new FileFolderUploadResult(
                false,
                $"上传文件夹失败：{ex.Message}",
                0,
                0,
                0,
                0,
                errorCode,
                FailedFiles: 1,
                Errors: [ex.Message]
            );
        }
    }

    private void UploadDirectoryRecursive(
        WindowsServerSession session,
        string shareName,
        string localDirectoryPath,
        string remoteDirectoryPath,
        IReadOnlyDictionary<string, UploadConflictDecision>? conflictDecisions,
        FolderUploadStats stats,
        WindowsFileTaskHandle? task,
        int totalFiles,
        bool updateTaskState,
        CancellationToken cancellationToken
    )
    {
        var progressTask = updateTaskState ? task : null;
        cancellationToken.ThrowIfCancellationRequested();
        task?.CancellationToken.ThrowIfCancellationRequested();

        EnsureRemoteDirectory(session, shareName, remoteDirectoryPath, stats, task, cancellationToken);

        var remoteItems = _bridge.SmbListDirectory(
            new SmbListDirectoryRequest(
                shareName,
                remoteDirectoryPath,
                session.ConnectionId,
                task?.CoreOperationId
            )
        );
        var remoteItemsByName = remoteItems.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var childDirectory in EnumerateDirectoriesSafe(localDirectoryPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            task?.CancellationToken.ThrowIfCancellationRequested();

            var directoryName = Path.GetFileName(childDirectory);
            var childRemotePath = AppendRemotePath(remoteDirectoryPath, directoryName);
            if (remoteItemsByName.TryGetValue(directoryName, out var existingItem))
            {
                if (existingItem.IsDir)
                {
                    if (TryResolveDecision(conflictDecisions, childDirectory, childRemotePath) == UploadConflictDecision.Skip)
                    {
                        stats.SkippedItems++;
                        continue;
                    }
                }
                else
                {
                    var decision = ResolveDecision(conflictDecisions, childDirectory, childRemotePath);
                    if (decision == UploadConflictDecision.Skip)
                    {
                        stats.SkippedItems++;
                        continue;
                    }

                    DeleteRemoteItem(session, shareName, existingItem, task, cancellationToken);
                    stats.ReplacedItems++;
                }
            }

            UploadDirectoryRecursive(
                session,
                shareName,
                childDirectory,
                childRemotePath,
                conflictDecisions,
                stats,
                task,
                totalFiles,
                updateTaskState,
                cancellationToken
            );
        }

        foreach (var filePath in EnumerateFilesSafe(localDirectoryPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            task?.CancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                stats.SkippedItems++;
                progressTask?.ReportProgress(
                    stats.UploadedFiles + stats.FailedFiles + stats.SkippedItems,
                    totalFiles,
                    $"正在上传文件夹：已处理 {stats.UploadedFiles + stats.FailedFiles + stats.SkippedItems}/{totalFiles} 个文件。"
                );
                continue;
            }

            var remoteFilePath = AppendRemotePath(remoteDirectoryPath, fileName);
            var replaceExisting = false;
            if (remoteItemsByName.TryGetValue(fileName, out var existingItem))
            {
                var decision = ResolveDecision(conflictDecisions, filePath, remoteFilePath);
                if (decision == UploadConflictDecision.Skip)
                {
                    stats.SkippedItems++;
                    progressTask?.ReportProgress(
                        stats.UploadedFiles + stats.FailedFiles + stats.SkippedItems,
                        totalFiles,
                        $"正在上传文件夹：已处理 {stats.UploadedFiles + stats.FailedFiles + stats.SkippedItems}/{totalFiles} 个文件。"
                    );
                    continue;
                }

                if (existingItem.IsDir)
                {
                    DeleteRemoteItem(session, shareName, existingItem, task, cancellationToken);
                    stats.ReplacedItems++;
                }
                else
                {
                    replaceExisting = true;
                }
            }

            try
            {
                _bridge.SmbUploadFile(
                    new SmbUploadFileRequest(
                        shareName,
                        filePath,
                        remoteFilePath,
                        replaceExisting,
                        session.ConnectionId,
                        task?.CoreOperationId
                    )
                );

                stats.UploadedFiles++;
                if (replaceExisting)
                {
                    stats.ReplacedItems++;
                }

                progressTask?.ReportProgress(
                    stats.UploadedFiles,
                    totalFiles,
                    $"正在上传文件夹：已上传 {stats.UploadedFiles} 个文件。"
                );
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex) || ex is IOException or UnauthorizedAccessException)
            {
                stats.FailedFiles++;
                stats.Errors.Add($"{fileName}：{ex.Message}");
                _diagnostics.Error(ex, $"上传文件夹中的文件失败，已继续处理下一项：{filePath}");
                progressTask?.ReportProgress(
                    stats.UploadedFiles + stats.FailedFiles + stats.SkippedItems,
                    totalFiles,
                    $"正在上传文件夹：已处理 {stats.UploadedFiles + stats.FailedFiles + stats.SkippedItems}/{totalFiles} 个文件。"
                );
            }
        }
    }

    private void EnsureRemoteDirectory(
        WindowsServerSession session,
        string shareName,
        string remoteDirectoryPath,
        FolderUploadStats stats,
        WindowsFileTaskHandle? task,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        task?.CancellationToken.ThrowIfCancellationRequested();

        try
        {
            _bridge.SmbCreateDirectory(
                new SmbCreateDirectoryRequest(
                    shareName,
                    remoteDirectoryPath,
                    session.ConnectionId,
                    task?.CoreOperationId
                )
            );
            stats.CreatedDirectories++;
        }
        catch (RynatCoreBridgeException ex) when (IsAlreadyExistsError(ex))
        {
            // 远端目录已存在时继续递归上传，冲突由子项逐个处理。
        }
    }

    private void DeleteRemoteItem(
        WindowsServerSession session,
        string shareName,
        SmbFileItem item,
        WindowsFileTaskHandle? task,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        task?.CancellationToken.ThrowIfCancellationRequested();

        if (item.IsDir)
        {
            var children = _bridge.SmbListDirectory(
                new SmbListDirectoryRequest(
                    shareName,
                    item.Path,
                    session.ConnectionId,
                    task?.CoreOperationId
                )
            );
            foreach (var child in children)
            {
                DeleteRemoteItem(session, shareName, child, task, cancellationToken);
            }
        }

        _bridge.SmbDelete(
            new SmbDeleteRequest(
                shareName,
                item.Path,
                item.IsDir,
                session.ConnectionId,
                task?.CoreOperationId
            )
        );
    }

    private static UploadConflictDecision ResolveDecision(
        IReadOnlyDictionary<string, UploadConflictDecision>? decisions,
        string localPath,
        string remotePath
    )
    {
        return TryResolveDecision(decisions, localPath, remotePath) ?? UploadConflictDecision.Skip;
    }

    private static UploadConflictDecision? TryResolveDecision(
        IReadOnlyDictionary<string, UploadConflictDecision>? decisions,
        string localPath,
        string remotePath
    )
    {
        if (decisions?.TryGetValue(localPath, out var localDecision) == true)
        {
            return localDecision;
        }

        if (decisions?.TryGetValue(remotePath, out var remoteDecision) == true)
        {
            return remoteDecision;
        }

        return null;
    }

    private static int CountFilesSafe(DirectoryInfo root, CancellationToken cancellationToken)
    {
        try
        {
            var count = 0;
            foreach (var _ in root.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                count++;
            }

            return count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private IEnumerable<string> EnumerateDirectoriesSafe(string localDirectoryPath)
    {
        try
        {
            return System.IO.Directory.EnumerateDirectories(localDirectoryPath).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _diagnostics.Error(ex, $"枚举本地子目录失败，已跳过：{localDirectoryPath}");
            return [];
        }
    }

    private IEnumerable<string> EnumerateFilesSafe(string localDirectoryPath)
    {
        try
        {
            return System.IO.Directory.EnumerateFiles(localDirectoryPath).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _diagnostics.Error(ex, $"枚举本地文件失败，已跳过：{localDirectoryPath}");
            return [];
        }
    }

    private static string BuildSummary(FolderUploadStats stats, string rootName)
    {
        var parts = new List<string>
        {
            $"文件夹“{rootName}”上传完成：文件 {stats.UploadedFiles} 个",
            $"新建文件夹 {stats.CreatedDirectories} 个"
        };
        if (stats.ReplacedItems > 0)
        {
            parts.Add($"覆盖 {stats.ReplacedItems} 项");
        }
        if (stats.SkippedItems > 0)
        {
            parts.Add($"跳过 {stats.SkippedItems} 项");
        }
        if (stats.FailedFiles > 0)
        {
            parts.Add($"失败 {stats.FailedFiles} 个文件");
        }

        return string.Join("，", parts) + "。";
    }

    private static string AppendRemotePath(string directoryPath, string name)
    {
        if (!IsSafeRemoteName(name))
        {
            throw new RynatCoreBridgeException("名称不能包含路径分隔符。", "path.invalid_name");
        }

        var normalizedDirectory = string.IsNullOrWhiteSpace(directoryPath) ? "/" : directoryPath.Trim();
        if (normalizedDirectory == "/")
        {
            return "/" + name.Trim();
        }

        return normalizedDirectory.TrimEnd('/') + "/" + name.Trim();
    }

    private static bool IsSafeRemoteName(string name)
    {
        var trimmed = name?.Trim();
        return !string.IsNullOrWhiteSpace(trimmed)
            && !trimmed.Contains('/')
            && !trimmed.Contains('\\')
            && trimmed != "."
            && trimmed != "..";
    }

    private static bool IsAlreadyExistsError(RynatCoreBridgeException ex)
    {
        var code = ex.ErrorCode ?? string.Empty;
        if (code.Equals("already_exists", StringComparison.OrdinalIgnoreCase)
            || code.Equals("exists", StringComparison.OrdinalIgnoreCase)
            || code.EndsWith(".already_exists", StringComparison.OrdinalIgnoreCase)
            || code.EndsWith(".exists", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var message = ex.Message ?? string.Empty;
        return message.Contains("目标已存在", StringComparison.OrdinalIgnoreCase)
            || message.Contains("already exists", StringComparison.OrdinalIgnoreCase)
            || message.Contains("file exists", StringComparison.OrdinalIgnoreCase)
            || message.Contains("已存在", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FolderUploadStats
    {
        public int UploadedFiles { get; set; }

        public int CreatedDirectories { get; set; }

        public int ReplacedItems { get; set; }

        public int SkippedItems { get; set; }

        public int FailedFiles { get; set; }

        public List<string> Errors { get; } = [];
    }
}
