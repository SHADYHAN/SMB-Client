namespace Rynat.WindowsClient.Domain;

public sealed record RemoteClipboardItem(
    RemoteClipboardMode Mode,
    IReadOnlyList<RemoteFileItem> Items
)
{
    public RemoteClipboardItem(RemoteClipboardMode mode, RemoteFileItem item)
        : this(mode, new[] { item })
    {
    }
}
