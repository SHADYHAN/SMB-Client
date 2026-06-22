using Rynat.Client;
using Rynat.WindowsClient.Infrastructure;
using Rynat.WindowsClient.UI.Main;

namespace Rynat.WindowsClient.AppServices.Links;

public sealed class QuickLinkLibraryService
{
    private readonly RynatCoreBridge _bridge;
    private readonly WindowsClientDiagnostics _diagnostics;

    public QuickLinkLibraryService(
        RynatCoreBridge bridge,
        WindowsClientDiagnostics diagnostics
    )
    {
        _bridge = bridge;
        _diagnostics = diagnostics;
    }

    public Task<QuickLinkLibraryResult> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var links = _bridge.ListQuickLinks();
                return new QuickLinkLibraryResult(
                    true,
                    $"已加载 {links.Length} 个收藏链接。",
                    Links: links
                );
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                _diagnostics.Error(ex, "加载收藏链接失败");
                return Failure(
                    $"加载收藏链接失败：{ex.Message}",
                    BridgeErrorCode(ex)
                );
            }
        }, cancellationToken);
    }

    public Task<QuickLinkLibraryResult> SaveForItemAsync(
        WindowsServerSession session,
        DirectoryItemViewModel item,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var link = _bridge.GenerateLink(
                    new GenerateLinkRequest(
                        session.Host,
                        item.ShareName,
                        item.RemotePath,
                        item.IsDirectory ? "dir" : "file"
                    )
                );

                return new QuickLinkLibraryResult(
                    true,
                    $"已收藏：{item.Name}",
                    Link: link
                );
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                _diagnostics.Error(ex, $"保存收藏链接失败：{item.DisplayPath}");
                return Failure(
                    $"保存收藏链接失败：{ex.Message}",
                    BridgeErrorCode(ex)
                );
            }
        }, cancellationToken);
    }

    public Task<QuickLinkLibraryResult> DeleteAsync(
        string linkId,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(linkId))
                {
                    return Failure("请选择要删除的收藏链接。", "quick_link.invalid_id");
                }

                _bridge.DeleteQuickLink(new DeleteQuickLinkRequest(linkId.Trim()));
                var links = _bridge.ListQuickLinks();
                return new QuickLinkLibraryResult(
                    true,
                    "收藏链接已删除。",
                    Links: links
                );
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                _diagnostics.Error(ex, $"删除收藏链接失败：{linkId}");
                return Failure(
                    $"删除收藏链接失败：{ex.Message}",
                    BridgeErrorCode(ex)
                );
            }
        }, cancellationToken);
    }

    private static QuickLinkLibraryResult Failure(string summary, string errorCode) =>
        new(false, summary, ErrorCode: errorCode);

    private static string BridgeErrorCode(Exception ex) =>
        BridgeExceptionClassifier.ErrorCodeFor(ex, "bridge.error");
}
