using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.FileTransfers;

public interface IFileTransferService
{
    Task<DragFilePayloadResult> CreateDragDownloadPayloadAsync(
        ServerSession session,
        IReadOnlyList<RemoteFileItem> items,
        CancellationToken cancellationToken = default
    );
}
