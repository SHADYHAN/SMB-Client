using System.Net;
using Rynat.Client;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Infrastructure;

namespace Rynat.WindowsClient.Services.LinkActivation;

public sealed class LinkActivationService : ILinkActivationService
{
    private readonly RynatCoreBridge _bridge;

    public LinkActivationService(RynatCoreBridge bridge)
    {
        _bridge = bridge;
    }

    public Task<LinkActivationResult> ActivateAsync(
        string rawLink,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalized = NormalizeRawLink(rawLink);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    return Failure("链接无效。", "link.empty");
                }

                var activation = _bridge.ActivateLink(new ActivateLinkRequest(normalized));
                cancellationToken.ThrowIfCancellationRequested();

                return new LinkActivationResult(
                    true,
                    new LinkOpenRequest(
                        activation.BrowseLocation.ServerHost,
                        activation.BrowseLocation.Share,
                        activation.BrowseLocation.RemotePath,
                        activation.BrowseLocation.SelectedPath,
                        string.Equals(activation.Target.Kind, "file", StringComparison.OrdinalIgnoreCase)
                    ),
                    "已打开链接。",
                    null
                );
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                return Failure("链接无效。", BridgeExceptionClassifier.ErrorCodeFor(ex));
            }
        }, cancellationToken);
    }

    private static LinkActivationResult Failure(string summary, string errorCode) =>
        new(false, null, summary, errorCode);

    public string? TryExtractStartupLink(IEnumerable<string> rawArguments)
    {
        foreach (var rawArgument in rawArguments)
        {
            var argument = rawArgument.Trim();
            if (IsSupportedLink(argument))
            {
                return argument;
            }

            var embeddedLink = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(IsSupportedLink);
            if (embeddedLink is not null)
            {
                return embeddedLink;
            }
        }

        return null;
    }

    public bool CanOpenWithSession(ServerSession session, LinkOpenRequest request)
    {
        var sessionEndpoint = ParseEndpoint(session.Host);
        var requestEndpoint = ParseEndpoint(request.ServerHost);
        return string.Equals(sessionEndpoint.Host, requestEndpoint.Host, StringComparison.OrdinalIgnoreCase)
            && sessionEndpoint.Port == requestEndpoint.Port;
    }

    private static string NormalizeRawLink(string rawLink)
    {
        var normalized = WebUtility.HtmlDecode(rawLink.Trim());
        return normalized.Replace("rynat://s/?", "rynat://s?", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedLink(string value)
    {
        return value.StartsWith("rynat://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static ServerEndpoint ParseEndpoint(string rawHost)
    {
        var normalized = rawHost.Trim();
        var portSeparator = normalized.LastIndexOf(':');
        if (portSeparator > 0
            && ushort.TryParse(normalized[(portSeparator + 1)..], out var explicitPort))
        {
            return new ServerEndpoint(normalized[..portSeparator].TrimEnd('.'), explicitPort);
        }

        return new ServerEndpoint(normalized.TrimEnd('.'), 445);
    }

    private readonly record struct ServerEndpoint(string Host, ushort Port);
}
