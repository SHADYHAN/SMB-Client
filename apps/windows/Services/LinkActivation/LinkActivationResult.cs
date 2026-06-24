using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.LinkActivation;

public sealed record LinkActivationResult(
    bool Succeeded,
    LinkOpenRequest? Request,
    string Summary,
    string? ErrorCode
);
