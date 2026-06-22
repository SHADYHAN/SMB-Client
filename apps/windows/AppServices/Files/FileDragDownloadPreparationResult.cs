using Rynat.WindowsClient.UI.Main;

namespace Rynat.WindowsClient.AppServices.Files;

public sealed record FileDragDownloadPreparedItem(
    DirectoryItemViewModel Source,
    string LocalPath
);

public sealed record FileDragDownloadPreparationResult(
    bool Succeeded,
    string Summary,
    IReadOnlyList<FileDragDownloadPreparedItem> Items,
    string? ErrorCode = null
);
