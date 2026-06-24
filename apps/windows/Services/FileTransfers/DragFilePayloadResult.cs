namespace Rynat.WindowsClient.Services.FileTransfers;

public sealed record DragFilePayloadResult(
    bool Succeeded,
    string Summary,
    IReadOnlyList<DragFilePayload> Files,
    string? ErrorCode = null
);
