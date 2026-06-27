using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Services.FileOperations;

namespace Rynat.WindowsClient.Services.FileTransfers;

public interface IFileTransferService
{
    Task<DragFilePayloadResult> DownloadFilesAsync(
        ServerSession session,
        IReadOnlyList<RemoteFileItem> items,
        IReadOnlyList<string> localPaths,
        bool replaceExisting,
        IProgress<FileBatchProgress>? progress = null,
        CancellationToken cancellationToken = default
    );

    Task<DragFilePayloadResult> CreateDragDownloadPayloadAsync(
        ServerSession session,
        IReadOnlyList<RemoteFileItem> items,
        CancellationToken cancellationToken = default
    );
}
