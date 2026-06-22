using Rynat.Client;
using Rynat.WindowsClient.Infrastructure;
using System.IO;

namespace Rynat.WindowsClient.AppServices.Bootstrap;

public sealed class AppBootstrapService
{
    private readonly RynatCoreBridge _bridge;

    public AppBootstrapService(RynatCoreBridge bridge)
    {
        _bridge = bridge;
    }

    public Task<AppBootstrapLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // 先打开持久化存储（rynat.sqlite），否则 Core 会回退到内存数据库，
                // 服务器配置/凭据/收藏重启后全部丢失，自动登录也失效。
                // 失败时降级到内存模式（Core 内部兜底），仅记日志，不阻断启动。
                try
                {
                    _bridge.OpenStore(new OpenStoreRequest(ResolveStorePath()));
                }
                catch (Exception storeEx) when (BridgeExceptionClassifier.IsBridgeFailure(storeEx))
                {
                    System.Diagnostics.Debug.WriteLine($"Open store failed, falling back to in-memory: {storeEx.Message}");
                }

                var snapshot = _bridge.AppBootstrap();
                cancellationToken.ThrowIfCancellationRequested();

                return new AppBootstrapLoadResult(
                    true,
                    snapshot,
                    BuildSummary(snapshot),
                    null
                );
            }
            catch (DllNotFoundException)
            {
                return new AppBootstrapLoadResult(
                    false,
                    null,
                    "当前缺少 rynat_core.dll。请先构建 Rust Core，并把 DLL 放到 Windows 客户端旁边，再继续联调真实流程。",
                    "bridge.dll_not_found"
                );
            }
            catch (OperationCanceledException)
            {
                return new AppBootstrapLoadResult(
                    false,
                    null,
                    "已取消加载启动信息。",
                    "bootstrap.cancelled"
                );
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                return new AppBootstrapLoadResult(
                    false,
                    null,
                    $"启动信息加载失败：{ex.Message}",
                    BridgeExceptionClassifier.ErrorCodeFor(ex)
                );
            }
            catch (Exception ex)
            {
                return new AppBootstrapLoadResult(
                    false,
                    null,
                    $"启动信息加载出现异常：{ex.Message}",
                    "bridge.unexpected"
                );
            }
        }, cancellationToken);
    }

    private static string ResolveStorePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(appData, "Rynat");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "rynat.sqlite");
    }

    public Task<ActiveProfileChangeResult> SetActiveProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var snapshot = _bridge.SetActiveServerProfile(
                    new SetActiveServerProfileRequest(profileId)
                );
                cancellationToken.ThrowIfCancellationRequested();

                return new ActiveProfileChangeResult(
                    true,
                    snapshot,
                    BuildSummary(snapshot),
                    null
                );
            }
            catch (DllNotFoundException)
            {
                return new ActiveProfileChangeResult(
                    false,
                    null,
                    "当前缺少 rynat_core.dll，因此暂时无法在 Windows 端切换当前服务器。",
                    "bridge.dll_not_found"
                );
            }
            catch (OperationCanceledException)
            {
                return new ActiveProfileChangeResult(
                    false,
                    null,
                    "已取消切换当前服务器。",
                    "profile.cancelled"
                );
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                return new ActiveProfileChangeResult(
                    false,
                    null,
                    $"切换当前服务器失败：{ex.Message}",
                    BridgeExceptionClassifier.ErrorCodeFor(ex)
                );
            }
            catch (Exception ex)
            {
                return new ActiveProfileChangeResult(
                    false,
                    null,
                    $"切换当前服务器时出现异常：{ex.Message}",
                    "bridge.unexpected"
                );
            }
        }, cancellationToken);
    }

    private static string BuildSummary(AppBootstrapState snapshot)
    {
        var profileCount = snapshot.ServerProfiles.Length;
        var activeServer = snapshot.ActiveServer?.DisplayName ?? "无";
        var activeCredential = snapshot.ActiveCredential?.Username ?? "无";
        return $"已加载 {profileCount} 个已保存的服务器。当前服务器：{activeServer}。当前凭据：{activeCredential}。";
    }
}
