namespace Rynat.WindowsClient.Platform.Clipboard;

public sealed class WindowsClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        System.Windows.Clipboard.SetText(text);
    }
}
