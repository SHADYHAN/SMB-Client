using System.IO;

namespace Rynat.WindowsClient.Services.FileTransfers;

public sealed record DragFilePayload(
    string FileName,
    ulong Size,
    DateTimeOffset? ModifiedAt,
    Func<Stream> OpenReadStream,
    string? LocalPath = null
);
