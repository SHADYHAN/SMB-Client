using Rynat.Client;

namespace Rynat.WindowsClient.AppServices.Links;

public sealed record QuickLinkLibraryResult(
    bool Succeeded,
    string Summary,
    IReadOnlyList<QuickLink>? Links = null,
    QuickLink? Link = null,
    string? ErrorCode = null
);
