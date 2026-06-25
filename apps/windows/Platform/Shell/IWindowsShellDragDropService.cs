using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Services.FileTransfers;
using System.Windows;

namespace Rynat.WindowsClient.Platform.Shell;

public interface IWindowsShellDragDropService
{
    bool CanStartDrag(IReadOnlyList<RemoteFileItem> selection);

    DragDropEffects StartDrag(
        object dragSource,
        RemoteDragPayload remotePayload,
        IReadOnlyList<DragFilePayload> files
    );
}
