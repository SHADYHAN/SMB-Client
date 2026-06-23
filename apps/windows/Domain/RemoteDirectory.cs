namespace Rynat.WindowsClient.Domain;

public sealed record RemoteDirectory(
    string Share,
    string Path,
    IReadOnlyList<RemoteFileItem> Items
);
