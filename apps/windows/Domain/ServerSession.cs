namespace Rynat.WindowsClient.Domain;

public sealed record ServerSession(
    string ConnectionId,
    string Host,
    string DialectLabel,
    IReadOnlyList<ServerShare> Shares
);
