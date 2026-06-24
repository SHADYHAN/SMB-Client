using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Services.LinkActivation;

namespace Rynat.WindowsClient.UI.Shell;

public sealed class LinkActivationCoordinator
{
    private readonly ILinkActivationService _linkActivationService;
    private LinkOpenRequest? _pendingLinkOpenRequest;

    public LinkActivationCoordinator(ILinkActivationService linkActivationService)
    {
        _linkActivationService = linkActivationService;
    }

    public async Task<string?> ActivateStartupArgumentsAsync(
        IReadOnlyList<string>? rawArguments,
        ServerSession? session,
        Func<LinkOpenRequest, Task<bool>> openLinkRequestAsync
    )
    {
        if (rawArguments is null || rawArguments.Count == 0)
        {
            return null;
        }

        var rawLink = _linkActivationService.TryExtractStartupLink(rawArguments);
        return string.IsNullOrWhiteSpace(rawLink)
            ? null
            : await ActivateLinkAsync(rawLink, session, openLinkRequestAsync);
    }

    public async Task<string?> ConsumePendingIfPossibleAsync(
        ServerSession? session,
        Func<LinkOpenRequest, Task<bool>> openLinkRequestAsync
    )
    {
        if (_pendingLinkOpenRequest is null
            || session is null
            || !_linkActivationService.CanOpenWithSession(session, _pendingLinkOpenRequest))
        {
            return null;
        }

        var request = _pendingLinkOpenRequest;
        _pendingLinkOpenRequest = null;
        return await openLinkRequestAsync(request) ? "已打开链接。" : null;
    }

    private async Task<string?> ActivateLinkAsync(
        string rawLink,
        ServerSession? session,
        Func<LinkOpenRequest, Task<bool>> openLinkRequestAsync
    )
    {
        var result = await _linkActivationService.ActivateAsync(rawLink);
        if (!result.Succeeded || result.Request is null)
        {
            return result.Summary;
        }

        if (session is null)
        {
            _pendingLinkOpenRequest = result.Request;
            return "请先登录对应服务器。";
        }

        if (!_linkActivationService.CanOpenWithSession(session, result.Request))
        {
            _pendingLinkOpenRequest = result.Request;
            return "请切换到对应服务器。";
        }

        return await openLinkRequestAsync(result.Request) ? "已打开链接。" : null;
    }
}
