using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Platform.Shell;

public interface IWindowsShellDragDropService
{
    bool CanStartDrag(IReadOnlyList<RemoteFileItem> selection);
}
