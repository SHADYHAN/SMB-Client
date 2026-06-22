namespace Rynat.WindowsClient.AppServices.Preview;

public enum PreviewDisplayState
{
    Placeholder,
    Loading,
    Ready,
    Empty,
    Error
}

public sealed record PreviewPaneState(
    string Title,
    string Description,
    string ActionHint,
    string? LocalImagePath = null,
    string? LocalPdfPath = null,
    string? LocalVideoPath = null,
    PreviewDisplayState DisplayState = PreviewDisplayState.Placeholder,
    string IconGlyph = "\uE8A5",
    string IconBrushKey = "RynatMutedBrush"
)
{
    public bool HasLocalImage => !string.IsNullOrWhiteSpace(LocalImagePath);

    public bool HasLocalPdf => !string.IsNullOrWhiteSpace(LocalPdfPath);

    public bool HasLocalVideo => !string.IsNullOrWhiteSpace(LocalVideoPath);
}
