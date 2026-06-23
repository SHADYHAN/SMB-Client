using Rynat.WindowsClient.UI.Main;

namespace Rynat.WindowsClient.AppServices.Files;

public enum FileClipboardMode
{
    Copy,
    Cut
}

public sealed record FileClipboardEntry(
    string Name,
    string DisplayPath,
    string RemotePath,
    string ShareName,
    bool IsDirectory
)
{
    public static FileClipboardEntry FromDirectoryItem(DirectoryItemViewModel item) =>
        new(
            item.Name,
            item.DisplayPath,
            item.RemotePath,
            item.ShareName,
            item.IsDirectory
        );
}

public sealed record FileClipboardState(
    FileClipboardMode Mode,
    IReadOnlyList<FileClipboardEntry> Entries
);
