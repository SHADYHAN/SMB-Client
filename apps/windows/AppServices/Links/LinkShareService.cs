using Rynat.Client;
using Rynat.WindowsClient.Infrastructure;
using Rynat.WindowsClient.UI.Main;

namespace Rynat.WindowsClient.AppServices.Links;

public sealed class LinkShareService
{
    private readonly RynatCoreBridge _bridge;

    public LinkShareService(RynatCoreBridge bridge)
    {
        _bridge = bridge;
    }

    public Task<LinkBuildResult> BuildForItemAsync(
        WindowsServerSession session,
        DirectoryItemViewModel item,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var link = _bridge.BuildLink(
                    new BuildLinkRequest(
                        session.Host,
                        item.ShareName,
                        item.RemotePath,
                        item.IsDirectory ? "dir" : "file"
                    )
                );
                cancellationToken.ThrowIfCancellationRequested();

                return new LinkBuildResult(
                    true,
                    link,
                    $"已生成链接，可粘贴到钉钉文档中：{item.Name}",
                    null
                );
            }
            catch (OperationCanceledException)
            {
                return new LinkBuildResult(
                    false,
                    null,
                    "已取消生成链接。",
                    "link.cancelled"
                );
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                return new LinkBuildResult(
                    false,
                    null,
                    $"生成链接失败：{ex.Message}",
                    BridgeExceptionClassifier.ErrorCodeFor(ex)
                );
            }
            catch (Exception ex)
            {
                return new LinkBuildResult(
                    false,
                    null,
                    $"生成链接时出现异常：{ex.Message}",
                    "bridge.unexpected"
                );
            }
        }, cancellationToken);
    }
}
