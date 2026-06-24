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
