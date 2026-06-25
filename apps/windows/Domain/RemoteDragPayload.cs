namespace Rynat.WindowsClient.Domain;

public sealed record RemoteDragPayload(IReadOnlyList<RemoteFileItem> Items)
{
    public const string DataFormat = "Rynat.WindowsClient.RemoteDragPayload";

    public RemoteDragPayload(RemoteFileItem item)
        : this(new[] { item })
    {
    }
}
