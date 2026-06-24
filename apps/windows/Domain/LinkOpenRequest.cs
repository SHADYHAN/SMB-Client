namespace Rynat.WindowsClient.Domain;

public sealed record LinkOpenRequest(
    string ServerHost,
    string Share,
    string DirectoryPath,
    string? SelectedPath,
    bool SelectsFile
);
