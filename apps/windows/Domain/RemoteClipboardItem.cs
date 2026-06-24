namespace Rynat.WindowsClient.Domain;

public sealed record RemoteClipboardItem(
    RemoteClipboardMode Mode,
    RemoteFileItem Item
);
