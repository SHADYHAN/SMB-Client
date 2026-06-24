using System.IO;
using Rynat.Client;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Infrastructure;

namespace Rynat.WindowsClient.Services.FileOperations;

public sealed class FileOperationService : IFileOperationService
{
    private readonly RynatCoreBridge _bridge;

    public FileOperationService(RynatCoreBridge bridge)
    {
        _bridge = bridge;
    }

    public Task<FileOperationResult> CreateDirectoryAsync(
        ServerSession session,
        string share,
        string parentPath,
        string name,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalizedName = name.Trim();
                if (!IsValidNewDirectoryName(normalizedName))
                {
                    return Failure("名称不可用。", "file.invalid_name");
                }

                _bridge.SmbCreateDirectory(new SmbCreateDirectoryRequest(
                    share,
                    JoinRemotePath(parentPath, normalizedName),
                    session.ConnectionId,
                    OperationId("mkdir")
                ));

                return new FileOperationResult(true, "文件夹已创建。");
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                return Failure("创建失败。", BridgeExceptionClassifier.ErrorCodeFor(ex));
            }
        }, cancellationToken);
    }

    public Task<FileOperationResult> DeleteAsync(
        ServerSession session,
        RemoteFileItem item,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _bridge.SmbDelete(new SmbDeleteRequest(
                    item.Share,
                    item.Path,
                    item.IsDirectory,
                    session.ConnectionId,
                    OperationId("delete")
                ));

                return new FileOperationResult(true, "已删除。");
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                return Failure("删除失败。", BridgeExceptionClassifier.ErrorCodeFor(ex));
            }
        }, cancellationToken);
    }

    public Task<FileOperationResult> RenameAsync(
        ServerSession session,
        RemoteFileItem item,
        string newName,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalizedName = newName.Trim();
                if (!IsValidNewDirectoryName(normalizedName))
                {
                    return Failure("名称不可用。", "file.invalid_name");
                }

                if (normalizedName.Equals(item.Name, StringComparison.CurrentCultureIgnoreCase))
                {
                    return new FileOperationResult(true, "名称未改变。");
                }

                _bridge.SmbRename(new SmbRenameRequest(
                    item.Share,
                    item.Path,
                    JoinRemotePath(ParentPath(item.Path), normalizedName),
                    session.ConnectionId,
                    OperationId("rename")
                ));

                return new FileOperationResult(true, "已重命名。");
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                return Failure("重命名失败。", BridgeExceptionClassifier.ErrorCodeFor(ex));
            }
        }, cancellationToken);
    }

    public Task<FileOperationResult> UploadFilesAsync(
        ServerSession session,
        string share,
        string parentPath,
        IReadOnlyList<string> localPaths,
        bool replaceExisting,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (localPaths.Count == 0)
                {
                    return Failure("请选择文件。", "upload.no_files");
                }

                if (localPaths.Any(Directory.Exists))
                {
                    return Failure("暂不支持上传文件夹。", "upload.directory_not_supported");
                }

                var uploaded = 0;
                foreach (var localPath in localPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!File.Exists(localPath))
                    {
                        return Failure("上传失败。", "upload.missing_file");
                    }

                    var fileName = Path.GetFileName(localPath);
                    if (!IsValidNewDirectoryName(fileName))
                    {
                        return Failure("文件名不可用。", "upload.invalid_name");
                    }

                    _bridge.SmbUploadFile(new SmbUploadFileRequest(
                        share,
                        localPath,
                        JoinRemotePath(parentPath, fileName),
                        replaceExisting,
                        session.ConnectionId,
                        OperationId("upload")
                    ));
                    uploaded++;
                }

                return new FileOperationResult(true, uploaded == 1 ? "上传完成。" : $"已上传 {uploaded} 个文件。");
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex) || ex is IOException or UnauthorizedAccessException)
            {
                return Failure("上传失败。", BridgeExceptionClassifier.ErrorCodeFor(ex, "upload.failed"));
            }
        }, cancellationToken);
    }

    private static FileOperationResult Failure(string summary, string errorCode) =>
        new(false, summary, errorCode);

    private static string OperationId(string prefix) =>
        prefix + "-" + Guid.NewGuid().ToString("N");

    private static bool IsValidNewDirectoryName(string name)
    {
        return !string.IsNullOrWhiteSpace(name)
            && name is not "." and not ".."
            && !name.Contains('/')
            && !name.Contains('\\');
    }

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
