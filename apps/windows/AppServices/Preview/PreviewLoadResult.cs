namespace Rynat.WindowsClient.AppServices.Preview;

public sealed record PreviewLoadResult(
    bool Succeeded,
    PreviewPaneState Pane,
    string Summary,
    string? ErrorCode
);
