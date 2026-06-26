using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using Rynat.Client;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Services.Cache;

namespace Rynat.WindowsClient.Services.Preview;

public sealed class PreviewService : IPreviewService
{
    private const uint DefaultPreviewEdgePx = 640;
    private const ulong ImagePreviewMaxBytes = 32UL * 1024 * 1024;
    private const int ImageThumbnailMaxEdgePx = 640;
    private const int VideoPosterMaxEdgePx = 640;
    private const ulong InlineVideoPreviewMaxBytes = 128UL * 1024 * 1024;
    private const long PreviewCacheMaxBytes = 2L * 1024 * 1024 * 1024;
    private static readonly TimeSpan PreviewCacheMaxAge = TimeSpan.FromDays(14);
    private readonly RynatCoreBridge _bridge;
    private readonly IThumbnailService _thumbnailService;

    public PreviewService(RynatCoreBridge bridge, IThumbnailService thumbnailService)
    {
        _bridge = bridge;
        _thumbnailService = thumbnailService;
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
                if (item.Size <= ImagePreviewMaxBytes)
                {
                    var originalImagePath = CachePreviewFile(
                        session,
                        item,
                        plan.CacheKey,
                        operationId,
                        ImagePreviewMaxBytes,
                        cancellationToken
                    );
                    localImagePath = CreateImageThumbnail(
                        originalImagePath,
                        item,
                        previewDirectory: Path.GetDirectoryName(originalImagePath)!,
                        ImageThumbnailMaxEdgePx,
                        cancellationToken
                    );
                }
                else
                {
                    message = "图片较大，暂不自动缓存预览。";
                }
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
                    localImagePath = CreateVideoPoster(
                        localVideoPath,
                        previewDirectory: Path.GetDirectoryName(localVideoPath)!,
                        VideoPosterMaxEdgePx,
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

    private static string CreateImageThumbnail(
        string sourcePath,
        RemoteFileItem item,
        string previewDirectory,
        int maxEdgePx,
        CancellationToken cancellationToken
    )
    {
        var extension = Path.GetExtension(item.Name);
        var thumbnailExtension = IsJpegExtension(extension) ? ".jpg" : ".png";
        var thumbnailPath = Path.Combine(
            previewDirectory,
            $"{Path.GetFileNameWithoutExtension(sourcePath)}-thumb-{maxEdgePx}{thumbnailExtension}"
        );
        if (IsCompleteThumbnailFile(thumbnailPath, sourcePath))
        {
            return thumbnailPath;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var bitmap = DecodeScaledBitmap(sourcePath, maxEdgePx);
        BitmapEncoder encoder = IsJpegExtension(extension)
            ? new JpegBitmapEncoder { QualityLevel = 88 }
            : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        cancellationToken.ThrowIfCancellationRequested();

        var partialPath = thumbnailPath + ".part";
        DeleteIfExists(partialPath);
        try
        {
            using (var stream = File.Create(partialPath))
            {
                encoder.Save(stream);
            }

            ReplaceWithCompletedFile(partialPath, thumbnailPath);
            File.SetLastWriteTimeUtc(thumbnailPath, File.GetLastWriteTimeUtc(sourcePath));
            return thumbnailPath;
        }
        finally
        {
            DeleteIfExists(partialPath);
        }
    }

    private static BitmapSource DecodeScaledBitmap(string sourcePath, int maxEdgePx)
    {
        var sourceUri = new Uri(sourcePath, UriKind.Absolute);
        var decoder = BitmapDecoder.Create(
            sourceUri,
            BitmapCreateOptions.DelayCreation,
            BitmapCacheOption.None
        );
        var frame = decoder.Frames[0];
        var decodeWidth = Math.Max(1, frame.PixelWidth);
        var decodeHeight = Math.Max(1, frame.PixelHeight);
        if (decodeWidth > maxEdgePx || decodeHeight > maxEdgePx)
        {
            var scale = Math.Min((double)maxEdgePx / decodeWidth, (double)maxEdgePx / decodeHeight);
            decodeWidth = Math.Max(1, (int)Math.Round(decodeWidth * scale));
            decodeHeight = Math.Max(1, (int)Math.Round(decodeHeight * scale));
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = sourceUri;
        bitmap.DecodePixelWidth = decodeWidth;
        bitmap.DecodePixelHeight = decodeHeight;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private string? CreateVideoPoster(
        string sourcePath,
        string previewDirectory,
        int maxEdgePx,
        CancellationToken cancellationToken
    )
    {
        var posterPath = Path.Combine(
            previewDirectory,
            $"{Path.GetFileNameWithoutExtension(sourcePath)}-poster-{maxEdgePx}.png"
        );
        if (IsCompleteThumbnailFile(posterPath, sourcePath))
        {
            return posterPath;
        }

        var partialPath = posterPath + ".part";
        DeleteIfExists(partialPath);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_thumbnailService.TryCreateThumbnail(sourcePath, partialPath, maxEdgePx))
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            ReplaceWithCompletedFile(partialPath, posterPath);
            File.SetLastWriteTimeUtc(posterPath, File.GetLastWriteTimeUtc(sourcePath));
            return posterPath;
        }
        finally
        {
            DeleteIfExists(partialPath);
        }
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
            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                try
                {
                    _bridge.SmbCancelOperation(new SmbCancelOperationRequest(operationId));
                }
                catch
                {
                    // Cancellation is best-effort across the native bridge.
                }
            });
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

    private static bool IsJpegExtension(string? extension) =>
        extension is not null
        && (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase));

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

    private static bool IsCompleteThumbnailFile(string thumbnailPath, string sourcePath)
    {
        try
        {
            if (!File.Exists(thumbnailPath))
            {
                return false;
            }

            var thumbnail = new FileInfo(thumbnailPath);
            if (thumbnail.Length <= 0)
            {
                return false;
            }

            return thumbnail.LastWriteTimeUtc >= File.GetLastWriteTimeUtc(sourcePath);
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
