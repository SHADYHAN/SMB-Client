using Rynat.Client;
using Rynat.WindowsClient.UI.Main;

namespace Rynat.WindowsClient.AppServices.Smb;

public sealed record SmbConnectFlowResult(
    bool Succeeded,
    WindowsServerSession? Session,
    AppBootstrapState? Snapshot,
    string Summary,
    string? ErrorCode
);
