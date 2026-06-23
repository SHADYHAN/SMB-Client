namespace Rynat.WindowsClient.AppServices.Files;

public enum UploadConflictDecision
{
    Replace,
    Skip
}

public sealed record FileUploadConflict(
    string LocalPath,
    string FileName,
    bool ExistingIsDirectory
);
