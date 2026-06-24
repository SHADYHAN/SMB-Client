using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Services.FileTransfers;

namespace Rynat.WindowsClient.Platform.Shell;

public interface IWindowsShellDragDropService
{
    bool CanStartDrag(IReadOnlyList<RemoteFileItem> selection);

    bool StartDrag(object dragSource, IReadOnlyList<DragFilePayload> files);
}
