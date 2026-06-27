namespace Rynat.WindowsClient.Services.FileOperations;

public sealed record FileBatchProgress(
    int Completed,
    int Total,
    string CurrentName
);
