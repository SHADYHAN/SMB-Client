namespace Rynat.WindowsClient.Domain;

public sealed record PreviewInfo(
    string ContentType,
    string? ThumbnailUrl,
    string? PlaybackUrl
);
