namespace Rynat.WindowsClient.AppServices.Files;

public sealed record FilePasteConflict(
    FileClipboardEntry Source,
    string TargetDisplayPath,
    string TargetRemotePath,
    bool ExistingIsDirectory
);
