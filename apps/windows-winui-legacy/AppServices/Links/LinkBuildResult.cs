using Rynat.Client;

namespace Rynat.WindowsClient.AppServices.Links;

public sealed record LinkBuildResult(
    bool Succeeded,
    QuickLink? Link,
    string Summary,
    string? ErrorCode
);
