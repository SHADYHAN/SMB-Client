namespace Rynat.WindowsClient.Platform.Clipboard;

public interface IClipboardService
{
    void SetText(string text);

    void SetShareLink(string displayUrl, string activationUrl);
}
