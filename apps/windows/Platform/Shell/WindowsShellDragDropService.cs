using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Platform.Shell;

public sealed class WindowsShellDragDropService : IWindowsShellDragDropService
{
    public bool CanStartDrag(IReadOnlyList<RemoteFileItem> selection)
    {
        return selection.Count > 0 && selection.All(item => !item.IsDirectory);
    }
}
