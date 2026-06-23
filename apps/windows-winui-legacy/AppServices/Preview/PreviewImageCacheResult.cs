namespace Rynat.WindowsClient.AppServices.Preview;

public sealed record PreviewImageCacheResult(
    bool Succeeded,
    string? LocalPath,
    string Summary,
    string? ErrorCode
);
