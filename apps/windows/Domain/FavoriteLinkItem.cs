namespace Rynat.WindowsClient.Domain;

public sealed record FavoriteLinkItem(
    string Id,
    string Name,
    string ServerHost,
    string Share,
    string Path,
    bool IsDirectory,
    string HttpUrl
);
