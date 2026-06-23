using Rynat.Client;
using Rynat.WindowsClient.AppServices.Preview;

namespace Rynat.WindowsClient.AppServices.Links;

public sealed record LinkActivationResult(
    bool Succeeded,
    LinkActivation? Activation,
    PreviewPaneState PreviewPane,
    string Summary,
    string? ErrorCode
);
