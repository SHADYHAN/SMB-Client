using Rynat.Client;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Infrastructure;

namespace Rynat.WindowsClient.Services.FileOperations;

public sealed class RemoteCopyMoveService : IRemoteCopyMoveService
{
    private readonly RynatCoreBridge _bridge;

    public RemoteCopyMoveService(RynatCoreBridge bridge)
    {
        _bridge = bridge;
    }

    public Task<FileOperationResult> CopyAsync(
        ServerSession session,
        RemoteFileItem item,
        string targetShare,
        string targetDirectory,
        bool replaceExisting,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var targetPath = JoinRemotePath(targetDirectory, item.Name);
                if (IsSameRemoteTarget(item.Share, item.Path, targetShare, targetPath))
                {
                    return Failure("不能复制到原位置。", "file.same_target");
                }
                if (item.IsDirectory && IsNestedDirectoryTarget(item.Share, item.Path, targetShare, targetPath))
                {
                    return Failure("不能复制到自身内部。", "file.nested_target");
                }

                if (item.IsDirectory)
                {
                    CopyDirectoryRecursive(session, item, targetShare, targetPath, replaceExisting, cancellationToken);
                    return new FileOperationResult(true, "已复制文件夹。");
                }

                if (replaceExisting && FindItem(session, targetShare, targetPath, cancellationToken)?.IsDirectory == true)
                {
                    DeleteTargetIfExists(session, targetShare, targetPath, cancellationToken);
                }

                _bridge.SmbCopyFile(new SmbCopyFileRequest(
                    item.Share,
                    item.Path,
                    targetShare,
                    targetPath,
                    replaceExisting,
                    session.ConnectionId,
                    OperationId("copy")
                ));

                return new FileOperationResult(true, "已复制。");
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                return Failure("复制失败。", BridgeExceptionClassifier.ErrorCodeFor(ex));
            }
        }, cancellationToken);
    }

    public Task<FileOperationResult> MoveAsync(
        ServerSession session,
        RemoteFileItem item,
        string targetShare,
        string targetDirectory,
        bool replaceExisting,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var targetPath = JoinRemotePath(targetDirectory, item.Name);
                if (IsSameRemoteTarget(item.Share, item.Path, targetShare, targetPath))
                {
                    return Failure("不能移动到原位置。", "file.same_target");
                }
                if (item.IsDirectory && IsNestedDirectoryTarget(item.Share, item.Path, targetShare, targetPath))
                {
                    return Failure("不能移动到自身内部。", "file.nested_target");
                }

                if (!item.Share.Equals(targetShare, StringComparison.OrdinalIgnoreCase))
                {
                    return Failure("暂不支持跨共享移动，请先复制再删除原文件。", "file.cross_share_move");
                }

                if (replaceExisting)
                {
                    DeleteTargetIfExists(session, targetShare, targetPath, cancellationToken);
                }

                _bridge.SmbRename(new SmbRenameRequest(
                    item.Share,
                    item.Path,
                    targetPath,
                    session.ConnectionId,
                    OperationId("move")
                ));

                return new FileOperationResult(true, item.IsDirectory ? "已移动文件夹。" : "已移动。");
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                return Failure("移动失败。", BridgeExceptionClassifier.ErrorCodeFor(ex));
            }
        }, cancellationToken);
    }

    private void CopyDirectoryRecursive(
        ServerSession session,
        RemoteFileItem sourceDirectory,
        string targetShare,
        string targetPath,
        bool replaceExisting,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (replaceExisting)
        {
            DeleteTargetIfExists(session, targetShare, targetPath, cancellationToken);
        }

        _bridge.SmbCreateDirectory(new SmbCreateDirectoryRequest(
            targetShare,
            targetPath,
            session.ConnectionId,
            OperationId("copy-mkdir")
        ));

        var children = _bridge.SmbListDirectory(new SmbListDirectoryRequest(
            sourceDirectory.Share,
            sourceDirectory.Path,
            session.ConnectionId,
            OperationId("copy-list")
        ));

        foreach (var child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var childItem = new RemoteFileItem(
                child.Name,
                sourceDirectory.Share,
                NormalizeRemotePath(child.Path),
                child.IsDir ? RemoteFileKind.Directory : RemoteFileKind.File,
                child.Size,
                child.ModifiedTime is null
                    ? null
                    : DateTimeOffset.FromUnixTimeSeconds(child.ModifiedTime.Value)
            );
            var childTargetPath = JoinRemotePath(targetPath, child.Name);
            if (childItem.IsDirectory)
            {
                CopyDirectoryRecursive(session, childItem, targetShare, childTargetPath, replaceExisting: false, cancellationToken);
                continue;
            }

            _bridge.SmbCopyFile(new SmbCopyFileRequest(
                childItem.Share,
                childItem.Path,
                targetShare,
                childTargetPath,
                ReplaceExisting: false,
                session.ConnectionId,
                OperationId("copy")
            ));
        }
    }

    private void DeleteTargetIfExists(
        ServerSession session,
        string share,
        string path,
        CancellationToken cancellationToken
    )
    {
        var existing = FindItem(session, share, path, cancellationToken);
        if (existing is null)
        {
            return;
        }

        DeleteRemotePathRecursive(session, existing, cancellationToken);
    }

    private void DeleteRemotePathRecursive(
        ServerSession session,
        RemoteFileItem item,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (item.IsDirectory)
        {
            var children = _bridge.SmbListDirectory(new SmbListDirectoryRequest(
                item.Share,
                item.Path,
                session.ConnectionId,
                OperationId("delete-list")
            ));

            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DeleteRemotePathRecursive(
                    session,
                    new RemoteFileItem(
                        child.Name,
                        item.Share,
                        NormalizeRemotePath(child.Path),
                        child.IsDir ? RemoteFileKind.Directory : RemoteFileKind.File,
                        child.Size,
                        child.ModifiedTime is null
                            ? null
                            : DateTimeOffset.FromUnixTimeSeconds(child.ModifiedTime.Value)
                    ),
                    cancellationToken
                );
            }
        }

        _bridge.SmbDelete(new SmbDeleteRequest(
            item.Share,
            item.Path,
            item.IsDirectory,
            session.ConnectionId,
            OperationId("replace-delete")
        ));
    }

    private RemoteFileItem? FindItem(
        ServerSession session,
        string share,
        string path,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parent = ParentPath(path);
        var name = PathName(path);
        var items = _bridge.SmbListDirectory(new SmbListDirectoryRequest(
            share,
            parent,
            session.ConnectionId,
            OperationId("find")
        ));
        var match = items.FirstOrDefault(item => item.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        return match is null
            ? null
            : new RemoteFileItem(
                match.Name,
                share,
                NormalizeRemotePath(match.Path),
                match.IsDir ? RemoteFileKind.Directory : RemoteFileKind.File,
                match.Size,
                match.ModifiedTime is null
                    ? null
                    : DateTimeOffset.FromUnixTimeSeconds(match.ModifiedTime.Value)
            );
    }

    private static FileOperationResult Failure(string summary, string errorCode) =>
        new(false, summary, errorCode);

    private static string OperationId(string prefix) =>
        prefix + "-" + Guid.NewGuid().ToString("N");

    private static string JoinRemotePath(string parentPath, string name)
    {
        var parent = NormalizeRemotePath(parentPath);
        return parent == "/" ? "/" + name : parent + "/" + name;
    }

    private static string ParentPath(string path)
    {
        var normalized = NormalizeRemotePath(path).TrimEnd('/');
        var index = normalized.LastIndexOf('/');
        return index <= 0 ? "/" : normalized[..index];
    }

    private static string PathName(string path)
    {
        var normalized = NormalizeRemotePath(path).TrimEnd('/');
        var index = normalized.LastIndexOf('/');
        return index < 0 ? normalized : normalized[(index + 1)..];
    }

    private static bool IsSameRemoteTarget(string leftShare, string leftPath, string rightShare, string rightPath)
    {
        return leftShare.Equals(rightShare, StringComparison.OrdinalIgnoreCase)
            && NormalizeRemotePath(leftPath).Equals(NormalizeRemotePath(rightPath), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNestedDirectoryTarget(string sourceShare, string sourcePath, string targetShare, string targetPath)
    {
        if (!sourceShare.Equals(targetShare, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var source = NormalizeRemotePath(sourcePath).TrimEnd('/');
        var target = NormalizeRemotePath(targetPath).TrimEnd('/');
        return target.StartsWith(source + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRemotePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        var normalized = path.Replace('\\', '/').Trim().TrimEnd('/');
        return normalized.StartsWith('/') ? normalized : "/" + normalized;
    }
}
