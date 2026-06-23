namespace Rynat.WindowsClient.Domain;

public sealed record RemoteFileItem(
    string Name,
    string Share,
    string Path,
    RemoteFileKind Kind,
    ulong Size,
    DateTimeOffset? ModifiedAt
)
{
    public bool IsDirectory => Kind == RemoteFileKind.Directory;
}
