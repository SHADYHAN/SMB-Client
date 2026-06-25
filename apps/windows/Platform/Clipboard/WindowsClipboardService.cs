using System.Net;
using System.Text;
using System.Windows;

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

    public void SetShareLink(string displayUrl, string activationUrl)
    {
        if (string.IsNullOrEmpty(displayUrl))
        {
            return;
        }

        var data = new DataObject();
        data.SetText(displayUrl, TextDataFormat.Text);
        data.SetText(displayUrl, TextDataFormat.UnicodeText);
        data.SetText(BuildHtmlFragment(displayUrl, activationUrl), TextDataFormat.Html);
        Clipboard.SetDataObject(data, true);
    }

    private static string BuildHtmlFragment(string displayUrl, string activationUrl)
    {
        var href = string.IsNullOrWhiteSpace(activationUrl) ? displayUrl : activationUrl;
        var fragment = $"<a href=\"{WebUtility.HtmlEncode(href)}\">{WebUtility.HtmlEncode(displayUrl)}</a>";
        const string startMarker = "<!--StartFragment-->";
        const string endMarker = "<!--EndFragment-->";
        var html = $"<html><body>{startMarker}{fragment}{endMarker}</body></html>";
        var headerTemplate =
            "Version:0.9\r\n" +
            "StartHTML:{0:0000000000}\r\n" +
            "EndHTML:{1:0000000000}\r\n" +
            "StartFragment:{2:0000000000}\r\n" +
            "EndFragment:{3:0000000000}\r\n";
        var dummyHeader = string.Format(headerTemplate, 0, 0, 0, 0);
        var startHtml = Encoding.UTF8.GetByteCount(dummyHeader);
        var startFragment = startHtml + Encoding.UTF8.GetByteCount(html[..html.IndexOf(startMarker, StringComparison.Ordinal)]) + Encoding.UTF8.GetByteCount(startMarker);
        var endFragment = startHtml + Encoding.UTF8.GetByteCount(html[..html.IndexOf(endMarker, StringComparison.Ordinal)]);
        var endHtml = startHtml + Encoding.UTF8.GetByteCount(html);
        var header = string.Format(headerTemplate, startHtml, endHtml, startFragment, endFragment);
        return header + html;
    }
}
