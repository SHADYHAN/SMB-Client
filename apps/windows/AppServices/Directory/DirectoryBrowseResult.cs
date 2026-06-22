using Rynat.WindowsClient.UI.Main;

namespace Rynat.WindowsClient.AppServices.Directory;

public sealed record DirectoryBrowseResult(
    bool Succeeded,
    IReadOnlyList<DirectoryItemViewModel> Items,
    string Summary,
    string? ErrorCode
);
