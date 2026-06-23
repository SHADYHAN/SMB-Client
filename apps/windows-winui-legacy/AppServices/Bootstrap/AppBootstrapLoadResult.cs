using Rynat.Client;

namespace Rynat.WindowsClient.AppServices.Bootstrap;

public sealed record AppBootstrapLoadResult(
    bool Succeeded,
    AppBootstrapState? Snapshot,
    string Summary,
    string? ErrorCode
);
