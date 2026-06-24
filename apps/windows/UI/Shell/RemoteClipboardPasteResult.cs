using Rynat.WindowsClient.Services.FileOperations;

namespace Rynat.WindowsClient.UI.Shell;

public sealed record RemoteClipboardPasteResult(
    FileOperationResult OperationResult,
    bool ClearClipboard
);
