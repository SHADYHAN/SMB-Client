using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.FileOperations;

public interface IRemoteCopyMoveService
{
    Task<FileOperationResult> CopyAsync(
        ServerSession session,
        RemoteFileItem item,
        string targetShare,
        string targetDirectory,
        bool replaceExisting,
        CancellationToken cancellationToken = default
    );

    Task<FileOperationResult> MoveAsync(
        ServerSession session,
        RemoteFileItem item,
        string targetShare,
        string targetDirectory,
        bool replaceExisting,
        CancellationToken cancellationToken = default
    );
}
