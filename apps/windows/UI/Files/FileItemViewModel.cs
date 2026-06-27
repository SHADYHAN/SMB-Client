using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.UI.Infrastructure;

namespace Rynat.WindowsClient.UI.Files;

public sealed class FileItemViewModel : ObservableObject
{
    public FileItemViewModel(RemoteFileItem item)
    {
        Item = item;
    }

    public RemoteFileItem Item { get; }

    public string Name => Item.Name;

    public string Type => Item.IsDirectory ? "文件夹" : "文件";

    public string IconGlyph => Item.IsDirectory ? "\uE8B7" : "\uE7C3";

    public string Size => Item.IsDirectory ? "" : FormatSize(Item.Size);

    public string ModifiedAt => Item.ModifiedAt?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? "";

    private static string FormatSize(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }
}
