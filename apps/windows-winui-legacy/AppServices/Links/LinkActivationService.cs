using Rynat.Client;
using Rynat.WindowsClient.AppServices.Preview;
using Rynat.WindowsClient.Infrastructure;
using System.Net;

namespace Rynat.WindowsClient.AppServices.Links;

public sealed class LinkActivationService
{
    private readonly RynatCoreBridge _bridge;
    private readonly PreviewEntryService _previewEntryService;

    public LinkActivationService(
        RynatCoreBridge bridge,
        PreviewEntryService previewEntryService
    )
    {
        _bridge = bridge;
        _previewEntryService = previewEntryService;
    }

    public Task<LinkActivationResult> ActivateAsync(
        string rawLink,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var normalizedRawLink = NormalizeRawLink(rawLink);
                cancellationToken.ThrowIfCancellationRequested();
                var activation = _bridge.ActivateLink(new ActivateLinkRequest(normalizedRawLink));
                cancellationToken.ThrowIfCancellationRequested();

                return new LinkActivationResult(
                    true,
                    activation,
                    _previewEntryService.BuildForActivation(activation),
                    $"已解析链接，目标路径：{activation.BrowseLocation.RemotePath}",
                    null
                );
            }
            catch (OperationCanceledException)
            {
                return new LinkActivationResult(
                    false,
                    null,
                    _previewEntryService.BuildArchitecturePlaceholder(),
                    "已取消打开外部链接。",
                    "link.cancelled"
                );
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                return new LinkActivationResult(
                    false,
                    null,
                    _previewEntryService.BuildArchitecturePlaceholder(),
                    $"外部链接激活失败：{ex.Message}",
                    BridgeExceptionClassifier.ErrorCodeFor(ex)
                );
            }
        }, cancellationToken);
    }

    private static string NormalizeRawLink(string rawLink)
    {
        if (string.IsNullOrWhiteSpace(rawLink))
        {
            return rawLink;
        }

        var normalized = WebUtility.HtmlDecode(rawLink.Trim());
        normalized = normalized.Replace("rynat://s/?", "rynat://s?", StringComparison.OrdinalIgnoreCase);
        return normalized;
    }
}
