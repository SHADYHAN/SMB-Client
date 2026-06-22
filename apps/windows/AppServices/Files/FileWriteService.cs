using System.IO;
using Rynat.Client;
using Rynat.WindowsClient.AppServices.Tasks;
using Rynat.WindowsClient.Infrastructure;
using Rynat.WindowsClient.UI.Main;

namespace Rynat.WindowsClient.AppServices.Files;

public sealed class FileWriteService
{
    private readonly RynatCoreBridge _bridge;
    private readonly WindowsClientDiagnostics _diagnostics;

    public FileWriteService(
        RynatCoreBridge bridge,
        WindowsClientDiagnostics diagnostics
    )
    {
        _bridge = bridge;
        _diagnostics = diagnostics;
    }

    public Task<FileOperationResult> CreateDirectoryAsync(
        WindowsServerSession session,
        string currentDisplayPath,
        string folderName,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var location = session.ResolveLocation(currentDisplayPath);
                if (location is null)
                {
                    return new FileOperationResult(
                        false,
                        "请先进入一个共享目录后再新建文件夹。",
                        "directory.invalid_location"
                    );
                }
                if (!IsSafeRemoteName(folderName))
                {
                    return new FileOperationResult(false, "名称不能包含路径分隔符。", "directory.invalid_name");
                }

                var remotePath = AppendRemotePath(location.RemotePath, folderName);
                _bridge.SmbCreateDirectory(
                    new SmbCreateDirectoryRequest(
                        location.ShareName,
                        remotePath,
                        session.ConnectionId
                    )
                );

                return new FileOperationResult(
                    true,
                    $"已创建文件夹：{folderName}"
                );
            }
            catch (OperationCanceledException)
            {
                return new FileOperationResult(false, "已取消创建文件夹。", "directory.cancelled");
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                _diagnostics.Error(ex, $"创建文件夹失败：{folderName}");
                return new FileOperationResult(
                    false,
                    $"创建文件夹失败：{ex.Message}",
                    BridgeExceptionClassifier.ErrorCodeFor(ex)
                );
            }
        }, cancellationToken);
    }

    public Task<FileOperationResult> RenameAsync(
        WindowsServerSession session,
        DirectoryItemViewModel item,
        string newName,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!IsSafeRemoteName(newName))
                {
                    return new FileOperationResult(false, "名称不能包含路径分隔符。", "rename.invalid_name");
                }

                var parentRemotePath = GetParentRemotePath(item.RemotePath);
                var targetPath = AppendRemotePath(parentRemotePath, newName);

                _bridge.SmbRename(
                    new SmbRenameRequest(
                        item.ShareName,
                        item.RemotePath,
                        targetPath,
                        session.ConnectionId
                    )
                );

                return new FileOperationResult(
                    true,
                    $"已重命名：{item.Name} -> {newName}"
                );
            }
            catch (OperationCanceledException)
            {
                return new FileOperationResult(false, "已取消重命名。", "rename.cancelled");
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                _diagnostics.Error(ex, $"重命名失败：{item.DisplayPath}");
                return new FileOperationResult(
                    false,
                    $"重命名失败：{ex.Message}",
                    BridgeExceptionClassifier.ErrorCodeFor(ex)
                );
            }
        }, cancellationToken);
    }

    public Task<FileOperationResult> DeleteAsync(
        WindowsServerSession session,
        DirectoryItemViewModel item,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                DeleteItemRecursive(session, item, cancellationToken);
                return new FileOperationResult(
                    true,
                    $"已删除：{item.Name}"
                );
            }
            catch (OperationCanceledException)
            {
                return new FileOperationResult(false, "已取消删除。", "delete.cancelled");
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                _diagnostics.Error(ex, $"删除失败：{item.DisplayPath}");
                return new FileOperationResult(
                    false,
                    $"删除失败：{ex.Message}",
                    BridgeExceptionClassifier.ErrorCodeFor(ex)
                );
            }
        }, cancellationToken);
    }

    public Task<FileOperationResult> UploadFilesAsync(
        WindowsServerSession session,
        string currentDisplayPath,
        IReadOnlyList<string> localPaths,
        IReadOnlyDictionary<string, UploadConflictDecision>? conflictDecisions = null,
        WindowsFileTaskHandle? task = null,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var location = session.ResolveLocation(currentDisplayPath);
                if (location is null)
                {
                    return new FileOperationResult(
                        false,
                        "请先进入一个共享目录后再上传文件。",
                        "upload.invalid_location"
                    );
                }

                var targetItems = _bridge.SmbListDirectory(
                    new SmbListDirectoryRequest(
                        location.ShareName,
                        location.RemotePath,
                        session.ConnectionId
                    )
                );
                var itemsByName = targetItems.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);

                var uploaded = 0;
                var replaced = 0;
                var skipped = 0;
                var failed = 0;
                var errors = new List<string>();
                var uploadedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var localPath in localPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    task?.CancellationToken.ThrowIfCancellationRequested();

                    if (!File.Exists(localPath))
                    {
                        skipped++;
                        task?.ReportProgress(uploaded + failed + skipped, localPaths.Count, $"上传中：已处理 {uploaded + failed + skipped}/{localPaths.Count} 项。");
                        continue;
                    }

                    var fileName = Path.GetFileName(localPath);
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        skipped++;
                        task?.ReportProgress(uploaded + failed + skipped, localPaths.Count, $"上传中：已处理 {uploaded + failed + skipped}/{localPaths.Count} 项。");
                        continue;
                    }
                    if (!uploadedNames.Add(fileName))
                    {
                        skipped++;
                        task?.ReportProgress(uploaded + failed + skipped, localPaths.Count, $"上传中：已处理 {uploaded + failed + skipped}/{localPaths.Count} 项。");
                        continue;
                    }

                    try
                    {
                        var remotePath = AppendRemotePath(location.RemotePath, fileName);
                        if (itemsByName.TryGetValue(fileName, out var existingItem))
                        {
                            if (conflictDecisions is null
                                || !conflictDecisions.TryGetValue(localPath, out var decision)
                                || decision == UploadConflictDecision.Skip)
                            {
                                skipped++;
                                task?.ReportProgress(uploaded + failed + skipped, localPaths.Count, $"上传中：已处理 {uploaded + failed + skipped}/{localPaths.Count} 项。");
                                continue;
                            }

                            if (existingItem.IsDir)
                            {
                                DeleteItemRecursive(
                                    session,
                                    new DirectoryItemViewModel(
                                        existingItem.Name,
                                        BuildDisplayPath(location.ShareName, existingItem.Path),
                                        existingItem.Path,
                                        location.ShareName,
                                        true,
                                        null,
                                        existingItem.ModifiedTime.HasValue
                                            ? DateTimeOffset.FromUnixTimeSeconds(existingItem.ModifiedTime.Value)
                                            : null
                                    ),
                                    cancellationToken
                                );
                            }
                        }

                        var replaceExisting = existingItem is not null && !existingItem.IsDir;
                        _bridge.SmbUploadFile(
                            new SmbUploadFileRequest(
                                location.ShareName,
                                localPath,
                                remotePath,
                                replaceExisting,
                                session.ConnectionId,
                                task?.CoreOperationId
                            )
                        );

                        itemsByName[fileName] = new SmbFileItem(fileName, remotePath, false, 0, null);
                        uploaded++;
                        if (replaceExisting)
                        {
                            replaced++;
                        }
                    }
                    catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex) || ex is IOException or UnauthorizedAccessException)
                    {
                        failed++;
                        errors.Add($"{fileName}：{ex.Message}");
                        _diagnostics.Error(ex, $"上传文件失败，已继续处理下一项：{localPath}");
                    }

                    task?.ReportProgress(uploaded + failed + skipped, localPaths.Count, $"上传中：已处理 {uploaded + failed + skipped}/{localPaths.Count} 项。");
                }

                if (uploaded == 0 && failed == 0 && skipped > 0)
                {
                    return new FileOperationResult(
                        false,
                        $"没有可上传的文件，已跳过 {skipped} 项。",
                        "upload.no_files"
                    );
                }

                var parts = new List<string> { $"上传完成 {uploaded} 项" };
                if (failed > 0)
                {
                    parts.Add($"失败 {failed} 项");
                }
                if (replaced > 0)
                {
                    parts.Add($"覆盖 {replaced} 项");
                }
                if (skipped > 0)
                {
                    parts.Add($"跳过 {skipped} 项");
                }
                var summary = string.Join("，", parts) + "。";

                return new FileOperationResult(
                    uploaded > 0 && failed == 0,
                    summary,
                    SucceededItems: uploaded,
                    SkippedItems: skipped,
                    ReplacedItems: replaced,
                    FailedItems: failed,
                    Errors: errors
                );
            }
            catch (OperationCanceledException)
            {
                return new FileOperationResult(false, "已取消上传文件。", "upload.cancelled");
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex) || ex is IOException or UnauthorizedAccessException)
            {
                _diagnostics.Error(ex, "上传文件失败");
                return new FileOperationResult(
                    false,
                    $"上传文件失败：{ex.Message}",
                    BridgeExceptionClassifier.ErrorCodeFor(ex, "upload.failed")
                );
            }
        }, cancellationToken);
    }

    public Task<IReadOnlyList<FileUploadConflict>> FindUploadConflictsAsync(
        WindowsServerSession session,
        string currentDisplayPath,
        IReadOnlyList<string> localPaths,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run<IReadOnlyList<FileUploadConflict>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var location = session.ResolveLocation(currentDisplayPath);
            if (location is null)
            {
                return [];
            }

            var targetItems = _bridge.SmbListDirectory(
                new SmbListDirectoryRequest(
                    location.ShareName,
                    location.RemotePath,
                    session.ConnectionId
                )
            );
            var itemsByName = targetItems.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
            var conflicts = new List<FileUploadConflict>();

            foreach (var localPath in localPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(localPath) && !System.IO.Directory.Exists(localPath))
                {
                    continue;
                }

                var fileName = Path.GetFileName(localPath);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                if (itemsByName.TryGetValue(fileName, out var existingItem))
                {
                    conflicts.Add(new FileUploadConflict(localPath, fileName, existingItem.IsDir));
                }
            }

            return conflicts;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<FilePasteConflict>> FindPasteConflictsAsync(
        WindowsServerSession session,
        string currentDisplayPath,
        FileClipboardState clipboard,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run<IReadOnlyList<FilePasteConflict>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var location = session.ResolveLocation(currentDisplayPath);
            if (location is null || clipboard.Entries.Count == 0)
            {
                return [];
            }

            if (clipboard.Mode == FileClipboardMode.Cut
                && clipboard.Entries.Any(entry => !string.Equals(
                    entry.ShareName,
                    location.ShareName,
                    StringComparison.OrdinalIgnoreCase
                )))
            {
                return [];
            }

            var targetItems = _bridge.SmbListDirectory(
                new SmbListDirectoryRequest(
                    location.ShareName,
                    location.RemotePath,
                    session.ConnectionId
                )
            );
            var itemsByName = targetItems.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
            var conflicts = new List<FilePasteConflict>();

            foreach (var entry in clipboard.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var targetRemotePath = AppendRemotePath(location.RemotePath, entry.Name);
                var targetDisplayPath = BuildDisplayPath(location.ShareName, targetRemotePath);
                if (IsSameRemoteLocation(entry.ShareName, entry.RemotePath, location.ShareName, targetRemotePath))
                {
                    continue;
                }

                if (clipboard.Mode == FileClipboardMode.Copy
                    && entry.IsDirectory
                    && IsDescendantRemoteLocation(location.ShareName, targetRemotePath, entry.ShareName, entry.RemotePath))
                {
                    continue;
                }

                if (clipboard.Mode == FileClipboardMode.Cut
                    && entry.IsDirectory
                    && IsDescendantRemoteLocation(entry.ShareName, targetRemotePath, entry.ShareName, entry.RemotePath))
                {
                    continue;
                }

                if (itemsByName.TryGetValue(entry.Name, out var existingItem))
                {
                    conflicts.Add(
                        new FilePasteConflict(
                            entry,
                            targetDisplayPath,
                            targetRemotePath,
                            existingItem.IsDir
                        )
                    );
                }
            }

            return conflicts;
        }, cancellationToken);
    }

    public Task<FileOperationResult> PasteAsync(
        WindowsServerSession session,
        string currentDisplayPath,
        FileClipboardState clipboard,
        IReadOnlyDictionary<string, UploadConflictDecision>? conflictDecisions = null,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var location = session.ResolveLocation(currentDisplayPath);
                if (location is null)
                {
                    return new FileOperationResult(
                        false,
                        "请先进入一个共享目录后再粘贴。",
                        "paste.invalid_location"
                    );
                }

                if (clipboard.Entries.Count == 0)
                {
                    return new FileOperationResult(
                        false,
                        "剪贴板中没有可粘贴的项目。",
                        "paste.empty_clipboard"
                    );
                }

                if (clipboard.Mode == FileClipboardMode.Cut
                    && clipboard.Entries.Any(entry => !string.Equals(
                        entry.ShareName,
                        location.ShareName,
                        StringComparison.OrdinalIgnoreCase
                    )))
                {
                    return new FileOperationResult(
                        false,
                        "跨共享移动暂不支持，请使用复制。",
                        "paste.cross_share_move_unsupported"
                    );
                }

                var targetItems = _bridge.SmbListDirectory(
                    new SmbListDirectoryRequest(
                        location.ShareName,
                        location.RemotePath,
                        session.ConnectionId
                    )
                );
                var itemsByName = targetItems.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);

                var pasted = 0;
                var replaced = 0;
                var skipped = 0;
                foreach (var entry in clipboard.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var targetRemotePath = AppendRemotePath(location.RemotePath, entry.Name);
                    if (IsSameRemoteLocation(entry.ShareName, entry.RemotePath, location.ShareName, targetRemotePath))
                    {
                        skipped++;
                        continue;
                    }

                    if (clipboard.Mode == FileClipboardMode.Copy
                        && entry.IsDirectory
                        && IsDescendantRemoteLocation(location.ShareName, targetRemotePath, entry.ShareName, entry.RemotePath))
                    {
                        skipped++;
                        continue;
                    }

                    if (clipboard.Mode == FileClipboardMode.Cut
                        && entry.IsDirectory
                        && IsDescendantRemoteLocation(entry.ShareName, targetRemotePath, entry.ShareName, entry.RemotePath))
                    {
                        skipped++;
                        continue;
                    }

                    if (itemsByName.TryGetValue(entry.Name, out var existingItem))
                    {
                        if (conflictDecisions is null
                            || !conflictDecisions.TryGetValue(entry.DisplayPath, out var decision)
                            || decision == UploadConflictDecision.Skip)
                        {
                            skipped++;
                            continue;
                        }

                        DeleteItemRecursive(
                            session,
                            new DirectoryItemViewModel(
                                existingItem.Name,
                                BuildDisplayPath(location.ShareName, existingItem.Path),
                                existingItem.Path,
                                location.ShareName,
                                existingItem.IsDir,
                                existingItem.IsDir ? null : existingItem.Size,
                                existingItem.ModifiedTime.HasValue
                                    ? DateTimeOffset.FromUnixTimeSeconds(existingItem.ModifiedTime.Value)
                                    : null
                            ),
                            cancellationToken
                        );
                        replaced++;
                    }

                    if (clipboard.Mode == FileClipboardMode.Copy)
                    {
                        CopyItemRecursive(
                            session,
                            entry.ShareName,
                            entry.RemotePath,
                            location.ShareName,
                            targetRemotePath,
                            entry.IsDirectory,
                            conflictDecisions?.ContainsKey(entry.DisplayPath) == true,
                            cancellationToken
                        );
                    }
                    else
                    {
                        _bridge.SmbRename(
                            new SmbRenameRequest(
                                entry.ShareName,
                                entry.RemotePath,
                                targetRemotePath,
                                session.ConnectionId
                            )
                        );
                    }

                    itemsByName[entry.Name] = new SmbFileItem(entry.Name, targetRemotePath, entry.IsDirectory, 0, null);
                    pasted++;
                }

                if (pasted == 0 && skipped > 0)
                {
                    return new FileOperationResult(
                        false,
                        $"没有可粘贴的项目，已跳过 {skipped} 项。",
                        "paste.no_items"
                    );
                }

                var parts = new List<string>
                {
                    clipboard.Mode == FileClipboardMode.Copy
                        ? $"复制完成 {pasted} 项"
                        : $"移动完成 {pasted} 项"
                };
                if (replaced > 0)
                {
                    parts.Add($"覆盖 {replaced} 项");
                }
                if (skipped > 0)
                {
                    parts.Add($"跳过 {skipped} 项");
                }
                var summary = string.Join("，", parts) + "。";

                return new FileOperationResult(
                    true,
                    summary,
                    SucceededItems: pasted,
                    SkippedItems: skipped,
                    ReplacedItems: replaced
                );
            }
            catch (OperationCanceledException)
            {
                return new FileOperationResult(false, "已取消粘贴。", "paste.cancelled");
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                _diagnostics.Error(ex, "粘贴失败");
                return new FileOperationResult(
                    false,
                    $"粘贴失败：{ex.Message}",
                    BridgeExceptionClassifier.ErrorCodeFor(ex, "paste.failed")
                );
            }
        }, cancellationToken);
    }

    private void DeleteItemRecursive(
        WindowsServerSession session,
        DirectoryItemViewModel item,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (item.IsDirectory)
        {
            var children = _bridge.SmbListDirectory(
                new SmbListDirectoryRequest(
                    item.ShareName,
                    item.RemotePath,
                    session.ConnectionId
                )
            );

            foreach (var child in children)
            {
                DeleteItemRecursive(
                    session,
                    new DirectoryItemViewModel(
                        child.Name,
                        BuildDisplayPath(item.ShareName, child.Path),
                        child.Path,
                        item.ShareName,
                        child.IsDir,
                        child.IsDir ? null : child.Size,
                        child.ModifiedTime.HasValue
                            ? DateTimeOffset.FromUnixTimeSeconds(child.ModifiedTime.Value)
                            : null
                    ),
                    cancellationToken
                );
            }
        }

        _bridge.SmbDelete(
            new SmbDeleteRequest(
                item.ShareName,
                item.RemotePath,
                item.IsDirectory,
                session.ConnectionId
            )
        );
    }

    private void CopyItemRecursive(
        WindowsServerSession session,
        string sourceShare,
        string sourceRemotePath,
        string targetShare,
        string targetRemotePath,
        bool isDirectory,
        bool replaceExisting,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSafeRemoteName(Path.GetFileName(targetRemotePath)))
        {
            throw new RynatCoreBridgeException("目标名称无效。", "path.invalid_name");
        }

        if (!isDirectory)
        {
            _bridge.SmbCopyFile(
                new SmbCopyFileRequest(
                    sourceShare,
                    sourceRemotePath,
                    targetShare,
                    targetRemotePath,
                    replaceExisting,
                    session.ConnectionId
                )
            );
            return;
        }

        var existingChildren = ListRemoteItemsByName(
            session,
            targetShare,
            targetRemotePath,
            allowMissing: true
        );

        try
        {
            _bridge.SmbCreateDirectory(
                new SmbCreateDirectoryRequest(
                    targetShare,
                    targetRemotePath,
                    session.ConnectionId
                )
            );
        }
        catch (RynatCoreBridgeException ex) when (IsAlreadyExistsError(ex))
        {
            // 目标子目录已经存在时继续复制子项，覆盖策略已在上层冲突处理中决定。
        }

        existingChildren ??= ListRemoteItemsByName(
            session,
            targetShare,
            targetRemotePath,
            allowMissing: false
        ) ?? [];

        var children = _bridge.SmbListDirectory(
            new SmbListDirectoryRequest(
                sourceShare,
                sourceRemotePath,
                session.ConnectionId
            )
        );

        foreach (var child in children)
        {
            var childTargetPath = AppendRemotePath(targetRemotePath, child.Name);
            var childReplaceExisting = replaceExisting;
            if (existingChildren.TryGetValue(child.Name, out var existingChild))
            {
                if (!replaceExisting)
                {
                    continue;
                }

                if (existingChild.IsDir != child.IsDir)
                {
                    DeleteItemRecursive(
                        session,
                        new DirectoryItemViewModel(
                            existingChild.Name,
                            BuildDisplayPath(targetShare, existingChild.Path),
                            existingChild.Path,
                            targetShare,
                            existingChild.IsDir,
                            existingChild.IsDir ? null : existingChild.Size,
                            existingChild.ModifiedTime.HasValue
                                ? DateTimeOffset.FromUnixTimeSeconds(existingChild.ModifiedTime.Value)
                                : null
                        ),
                        cancellationToken
                    );
                }
                else if (!child.IsDir)
                {
                    childReplaceExisting = true;
                }
            }

            CopyItemRecursive(
                session,
                sourceShare,
                child.Path,
                targetShare,
                childTargetPath,
                child.IsDir,
                childReplaceExisting,
                cancellationToken
            );
        }
    }

    private Dictionary<string, SmbFileItem>? ListRemoteItemsByName(
        WindowsServerSession session,
        string shareName,
        string remoteDirectoryPath,
        bool allowMissing
    )
    {
        try
        {
            return _bridge.SmbListDirectory(
                    new SmbListDirectoryRequest(
                        shareName,
                        remoteDirectoryPath,
                        session.ConnectionId
                    )
                )
                .ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        }
        catch (RynatCoreBridgeException ex) when (allowMissing && IsNotFoundError(ex))
        {
            return null;
        }
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

    private static string GetParentRemotePath(string remotePath)
    {
        var normalized = NormalizeRemotePath(remotePath).TrimEnd('/');
        if (normalized == "/")
        {
            return "/";
        }

        var lastSlashIndex = normalized.LastIndexOf('/');
        if (lastSlashIndex <= 0)
        {
            return "/";
        }

        return normalized[..lastSlashIndex];
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

    private static string BuildDisplayPath(string shareName, string remotePath)
    {
        var normalizedRemote = string.IsNullOrWhiteSpace(remotePath) || remotePath == "/"
            ? string.Empty
            : remotePath.Trim();

        if (!normalizedRemote.StartsWith('/') && normalizedRemote.Length > 0)
        {
            normalizedRemote = "/" + normalizedRemote;
        }

        return "/" + shareName + normalizedRemote;
    }

    private static bool IsSameRemoteLocation(
        string leftShare,
        string leftRemotePath,
        string rightShare,
        string rightRemotePath
    )
    {
        return string.Equals(leftShare, rightShare, StringComparison.OrdinalIgnoreCase)
               && string.Equals(
                   NormalizeRemotePath(leftRemotePath),
                   NormalizeRemotePath(rightRemotePath),
                   StringComparison.OrdinalIgnoreCase
               );
    }

    private static bool IsDescendantRemoteLocation(
        string candidateShare,
        string candidateRemotePath,
        string ancestorShare,
        string ancestorRemotePath
    )
    {
        if (!string.Equals(candidateShare, ancestorShare, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = NormalizeRemotePath(candidateRemotePath).TrimEnd('/');
        var ancestor = NormalizeRemotePath(ancestorRemotePath).TrimEnd('/');
        if (ancestor == string.Empty || ancestor == "/")
        {
            return candidate != "/";
        }

        return candidate.StartsWith(ancestor + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRemotePath(string remotePath)
    {
        var normalized = string.IsNullOrWhiteSpace(remotePath) ? "/" : remotePath.Trim();
        return normalized.StartsWith('/') ? normalized : "/" + normalized;
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

    private static bool IsNotFoundError(RynatCoreBridgeException ex)
    {
        var code = ex.ErrorCode ?? string.Empty;
        if (code.Equals("not_found", StringComparison.OrdinalIgnoreCase)
            || code.EndsWith(".not_found", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var message = ex.Message ?? string.Empty;
        return message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("找不到", StringComparison.OrdinalIgnoreCase)
            || message.Contains("不存在", StringComparison.OrdinalIgnoreCase);
    }
}
