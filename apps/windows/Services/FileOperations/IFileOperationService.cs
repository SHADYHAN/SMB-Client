using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.FileOperations;

public interface IFileOperationService
{
    Task<FileOperationResult> CreateDirectoryAsync(
        ServerSession session,
        string share,
        string parentPath,
        string name,
        CancellationToken cancellationToken = default
    );

    Task<FileOperationResult> DeleteAsync(
        ServerSession session,
        RemoteFileItem item,
        CancellationToken cancellationToken = default
    );

    Task<FileOperationResult> UploadFilesAsync(
        ServerSession session,
        string share,
        string parentPath,
        IReadOnlyList<string> localPaths,
        bool replaceExisting,
        CancellationToken cancellationToken = default
    );
}
