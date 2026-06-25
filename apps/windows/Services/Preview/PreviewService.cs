using System.IO;
using System.Security.Cryptography;
using System.Text;
using Rynat.Client;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Services.Cache;

namespace Rynat.WindowsClient.Services.Preview;

public sealed class PreviewService : IPreviewService
{
    private const uint DefaultPreviewEdgePx = 640;
    private const ulong ImagePreviewMaxBytes = 32UL * 1024 * 1024;
    private const ulong InlineVideoPreviewMaxBytes = 128UL * 1024 * 1024;
    private const long PreviewCacheMaxBytes = 2L * 1024 * 1024 * 1024;
    private static readonly TimeSpan PreviewCacheMaxAge = TimeSpan.FromDays(14);
    private readonly RynatCoreBridge _bridge;

    public PreviewService(RynatCoreBridge bridge)
    {
        _bridge = bridge;
    }

    public Task<PreviewInfo> PlanAsync(
        ServerSession session,
        RemoteFileItem item,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operationId = "preview-" + Guid.NewGuid().ToString("N");
            var plan = _bridge.PreviewPlan(new PreviewPlanRequest(
                session.Host,
                item.Share,
                item.Path,
                item.IsDirectory ? "directory" : "file",
                DefaultPreviewEdgePx
            ));
            cancellationToken.ThrowIfCancellationRequested();

            string? localImagePath = null;
            string? localVideoPath = null;
            string? message = null;
            if (IsImage(plan.ContentType))
            {
                localImagePath = CachePreviewFile(
                    session,
                    item,
                    plan.CacheKey,
                    operationId,
                    ImagePreviewMaxBytes,
                    cancellationToken
                );
            }
            else if (IsVideo(plan.ContentType))
            {
                if (item.Size <= InlineVideoPreviewMaxBytes)
                {
                    localVideoPath = CachePreviewFile(
                        session,
                        item,
                        plan.CacheKey,
                        operationId,
                        InlineVideoPreviewMaxBytes,
                        cancellationToken
                    );
                }
                else
                {
                    message = "视频较大，暂不自动缓存预览。";
                }
            }

            return new PreviewInfo(
                plan.ContentType,
                plan.Thumbnail?.Url,
                plan.Playback?.Url,
                localImagePath,
                localVideoPath,
                message
            );
        }, cancellationToken);
    }

    private string CachePreviewFile(
        ServerSession session,
        RemoteFileItem item,
        string cacheKey,
        string operationId,
        ulong maxBytes,
        CancellationToken cancellationToken
    )
    {
        var previewDirectory = WindowsCacheCleanupService.AppCacheDirectory(
            "PreviewCache",
            SafeFileName(session.ConnectionId)
        );
        System.IO.Directory.CreateDirectory(previewDirectory);
        WindowsCacheCleanupService.CleanupDirectory(
            previewDirectory,
            PreviewCacheMaxBytes,
            PreviewCacheMaxAge
        );

        var extension = Path.GetExtension(item.Name);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".bin";
        }

        var localPath = Path.Combine(
            previewDirectory,
            $"{SafeFileName(item.Name)}-{StableKey(session.Host, item.Share, item.Path, cacheKey)}{extension}"
        );
        if (IsCompleteCacheFile(localPath, item.Size, maxBytes))
        {
            return localPath;
        }

        var partialPath = localPath + ".part";
        DeleteIfExists(partialPath);
        try
        {
            var cached = _bridge.SmbCacheFile(new SmbCacheFileRequest(
                item.Share,
                item.Path,
                partialPath,
                maxBytes,
                session.ConnectionId,
                operationId
            ));
            cancellationToken.ThrowIfCancellationRequested();
            ReplaceWithCompletedFile(cached.LocalPath, localPath);
            return localPath;
        }
        finally
        {
            DeleteIfExists(partialPath);
        }
    }

    private static bool IsImage(string contentType) =>
        contentType.Equals("image", StringComparison.OrdinalIgnoreCase);

    private static bool IsVideo(string contentType) =>
        contentType.Equals("video", StringComparison.OrdinalIgnoreCase);

    private static bool IsCompleteCacheFile(string localPath, ulong expectedSize, ulong maxBytes)
    {
        try
        {
            if (!File.Exists(localPath))
            {
                return false;
            }

            var info = new FileInfo(localPath);
            var expectedCachedBytes = Math.Min(expectedSize, maxBytes);
            return info.Length > 0 && unchecked((ulong)info.Length) >= expectedCachedBytes;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void ReplaceWithCompletedFile(string partialPath, string completedPath)
    {
        if (File.Exists(completedPath))
        {
            File.Delete(completedPath);
        }

        File.Move(partialPath, completedPath);
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat(value.Select(ch => invalid.Contains(ch) ? '_' : ch));
        return string.IsNullOrWhiteSpace(safe) ? "preview" : safe;
    }

    private static string StableKey(params string[] parts)
    {
        var input = string.Join("\n", parts);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }
}
