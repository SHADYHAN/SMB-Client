using System.IO;
using Rynat.WindowsClient.AppServices.Cache;
using Rynat.Client;
using Rynat.WindowsClient.Infrastructure;
using Rynat.WindowsClient.PlatformIntegration.Preview;
using Rynat.WindowsClient.UI.Main;

namespace Rynat.WindowsClient.AppServices.Preview;

public sealed class PreviewEntryService
{
    private const uint DefaultPreviewEdgePx = 512;
    private const ulong DefaultPreviewCacheMaxBytes = 32UL * 1024 * 1024;
    private const ulong DefaultVideoPreviewMaxBytes = 512UL * 1024 * 1024;
    private const long DefaultTotalCacheMaxBytes = 2L * 1024 * 1024 * 1024;
    private readonly RynatCoreBridge _bridge;
    private readonly IWindowsPreviewSurface _previewSurface;
    private readonly WindowsCacheManagementService? _cacheManagementService;
    private readonly object _previewSync = new();
    private CancellationTokenSource? _activePreviewCts;
    private string? _activePreviewOperationId;

    public PreviewEntryService(
        RynatCoreBridge bridge,
        IWindowsPreviewSurface previewSurface,
        WindowsCacheManagementService? cacheManagementService = null
    )
    {
        _bridge = bridge;
        _previewSurface = previewSurface;
        _cacheManagementService = cacheManagementService;
    }

    public PreviewPaneState BuildArchitecturePlaceholder()
    {
        return _previewSurface.BuildPlaceholder(
            "预览",
            string.Empty
        );
    }

    public PreviewPaneState BuildForActivation(LinkActivation activation)
    {
        return _previewSurface.BuildFromPlan(
            activation.PreviewPlan,
            activation.BrowseLocation.RemotePath
        );
    }

    public void CancelActivePreview()
    {
        string? previousOperationId;
        CancellationTokenSource? previousCts;
        lock (_previewSync)
        {
            previousOperationId = _activePreviewOperationId;
            previousCts = _activePreviewCts;
            _activePreviewOperationId = null;
            _activePreviewCts = null;
        }

        previousCts?.Cancel();
        previousCts?.Dispose();
        if (!string.IsNullOrWhiteSpace(previousOperationId))
        {
            try
            {
                _bridge.SmbCancelOperation(new SmbCancelOperationRequest(previousOperationId));
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
            }
        }
    }

    public Task<PreviewLoadResult> LoadForItemAsync(
        WindowsServerSession session,
        DirectoryItemViewModel item,
        CancellationToken cancellationToken = default
    )
    {
        var operationId = "preview-" + Guid.NewGuid().ToString("N");
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancelActivePreview(operationId, linkedCts);

        return Task.Run(() =>
        {
            var token = linkedCts.Token;
            if (item.IsDirectory)
            {
                return new PreviewLoadResult(
                    true,
                    new PreviewPaneState(
                        item.Name,
                        BuildDirectoryPreviewDescription(session, item),
                        string.Empty,
                        DisplayState: PreviewDisplayState.Empty,
                        IconGlyph: "\uE8B7",
                        IconBrushKey: "RynatFolderBrush"
                    ),
                    item.Name,
                    null
                );
            }

            try
            {
                token.ThrowIfCancellationRequested();
                var plan = _bridge.PreviewPlan(
                    new PreviewPlanRequest(
                        session.Host,
                        item.ShareName,
                        item.RemotePath,
                        "file",
                        DefaultPreviewEdgePx
                    )
                );

                var pane = _previewSurface.BuildFromPlan(plan, item.RemotePath);
                var summary = $"已获取 {item.Name} 的预览信息。";

                if (string.Equals(plan.ContentType, "image", StringComparison.OrdinalIgnoreCase))
                {
                    var result = CachePreviewFile(session, item, plan.CacheKey, operationId, token);
                    if (result.Succeeded && !string.IsNullOrWhiteSpace(result.LocalPath))
                    {
                        pane = pane with { LocalImagePath = result.LocalPath };
                        summary = $"已完成 {item.Name} 的图片预览缓存。";
                    }
                    else
                    {
                        summary = result.Summary;
                    }
                }
                else if (string.Equals(plan.ContentType, "pdf", StringComparison.OrdinalIgnoreCase))
                {
                    var result = CachePreviewFile(session, item, plan.CacheKey, operationId, token);
                    if (result.Succeeded && !string.IsNullOrWhiteSpace(result.LocalPath))
                    {
                        pane = pane with { LocalPdfPath = result.LocalPath };
                        summary = $"已完成 {item.Name} 的 PDF 预览缓存。";
                    }
                    else
                    {
                        summary = result.Summary;
                    }
                }
                else if (string.Equals(plan.ContentType, "video", StringComparison.OrdinalIgnoreCase))
                {
                    var result = CachePreviewFile(
                        session,
                        item,
                        plan.CacheKey,
                        operationId,
                        DefaultVideoPreviewMaxBytes,
                        token
                    );
                    if (result.Succeeded && !string.IsNullOrWhiteSpace(result.LocalPath))
                    {
                        pane = pane with { LocalVideoPath = result.LocalPath };
                        summary = $"已完成 {item.Name} 的视频预览缓存。";
                    }
                    else
                    {
                        summary = result.Summary;
                    }
                }

                return new PreviewLoadResult(
                    true,
                    pane,
                    summary,
                    null
                );
            }
            catch (OperationCanceledException)
            {
                return new PreviewLoadResult(
                    false,
                    new PreviewPaneState(
                        item.Name,
                        item.DisplayPath,
                        string.Empty,
                        DisplayState: PreviewDisplayState.Empty,
                        IconGlyph: PreviewIconGlyphForItem(item),
                        IconBrushKey: PreviewIconBrushKeyForItem(item)
                    ),
                    item.Name,
                    "preview.cancelled"
                );
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                return new PreviewLoadResult(
                    false,
                    new PreviewPaneState(
                        item.Name,
                        item.DisplayPath,
                        "预览失败",
                        DisplayState: PreviewDisplayState.Error,
                        IconGlyph: "\uE8A5",
                        IconBrushKey: "RynatMutedBrush"
                    ),
                    $"预览加载失败：{ex.Message}",
                    BridgeExceptionClassifier.ErrorCodeFor(ex)
                );
            }
            finally
            {
                ClearActivePreview(operationId, linkedCts);
            }
        });
    }

    private static string BuildDirectoryPreviewDescription(
        WindowsServerSession session,
        DirectoryItemViewModel item
    )
    {
        if (session.HasCached(item.DisplayPath))
        {
            return $"{session.CachedItemsFor(item.DisplayPath).Count} 项 · 文件夹";
        }

        return "文件夹";
    }

    private PreviewImageCacheResult CachePreviewFile(
        WindowsServerSession session,
        DirectoryItemViewModel item,
        string cacheKey,
        string operationId,
        CancellationToken cancellationToken
    )
    {
        return CachePreviewFile(
            session,
            item,
            cacheKey,
            operationId,
            DefaultPreviewCacheMaxBytes,
            cancellationToken
        );
    }

    private static string PreviewIconGlyphForItem(DirectoryItemViewModel item)
    {
        if (item.IsDirectory)
        {
            return "\uE8B7";
        }

        var extension = Path.GetExtension(item.Name).TrimStart('.').ToLowerInvariant();
        return extension switch
        {
            "jpg" or "jpeg" or "png" or "gif" or "webp" or "heic" or "heif" or "avif" => "\uEB9F",
            "pdf" => "\uEA90",
            "mp4" or "mov" or "m4v" or "mkv" or "avi" or "webm" => "\uE714",
            _ => "\uE8A5"
        };
    }

    private static string PreviewIconBrushKeyForItem(DirectoryItemViewModel item)
    {
        if (item.IsDirectory)
        {
            return "RynatFolderBrush";
        }

        var extension = Path.GetExtension(item.Name).TrimStart('.').ToLowerInvariant();
        return extension switch
        {
            "jpg" or "jpeg" or "png" or "gif" or "webp" or "heic" or "heif" or "avif" => "RynatAccentBrush",
            "pdf" or "mp4" or "mov" or "m4v" or "mkv" or "avi" or "webm" => "RynatAccentBrush",
            _ => "RynatMutedBrush"
        };
    }

    private PreviewImageCacheResult CachePreviewFile(
        WindowsServerSession session,
        DirectoryItemViewModel item,
        string cacheKey,
        string operationId,
        ulong maxBytes,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var localPath = string.Empty;
        try
        {
            var previewDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Rynat",
                "PreviewCache",
                session.Profile.Id
            );
            System.IO.Directory.CreateDirectory(previewDirectory);

            var extension = Path.GetExtension(item.Name);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".bin";
            }

            var safeName = string.Concat(
                item.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)
            );
            localPath = Path.Combine(
                previewDirectory,
                $"{safeName}-{StableCacheKey.FromParts(session.Profile.Id, item.ShareName, item.RemotePath, cacheKey)}{extension}"
            );

            if (File.Exists(localPath))
            {
                var fileInfo = new FileInfo(localPath);
                if (IsCompleteCacheFile(item, fileInfo))
                {
                    return new PreviewImageCacheResult(
                        true,
                        localPath,
                        $"已复用 {item.Name} 的本地预览缓存。",
                        null
                    );
                }
            }

            var partialPath = localPath + ".part";
            DeleteIfExists(partialPath);
            var cached = _bridge.SmbCacheFile(
                new SmbCacheFileRequest(
                    item.ShareName,
                    item.RemotePath,
                    partialPath,
                    maxBytes,
                    session.ConnectionId,
                    operationId
                )
            );
            cancellationToken.ThrowIfCancellationRequested();
            ReplaceWithCompletedFile(cached.LocalPath, localPath);
            CleanupPreviewCache(previewDirectory, cancellationToken);

            return new PreviewImageCacheResult(
                true,
                localPath,
                $"已缓存 {item.Name} 的预览文件。",
                null
            );
        }
        catch (OperationCanceledException)
        {
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                DeleteIfExists(localPath + ".part");
            }
            return new PreviewImageCacheResult(
                false,
                null,
                $"已取消 {item.Name} 的预览缓存。",
                "preview.cancelled"
            );
        }
        catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex) || ex is IOException or UnauthorizedAccessException)
        {
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                DeleteIfExists(localPath + ".part");
            }
            return new PreviewImageCacheResult(
                false,
                null,
                $"预览信息已获取，但本地缓存失败：{ex.Message}",
                BridgeExceptionClassifier.ErrorCodeFor(ex, "preview.cache_failed")
            );
        }
    }

    private void CancelActivePreview(
        string operationId,
        CancellationTokenSource linkedCts
    )
    {
        string? previousOperationId;
        CancellationTokenSource? previousCts;
        lock (_previewSync)
        {
            previousOperationId = _activePreviewOperationId;
            previousCts = _activePreviewCts;
            _activePreviewOperationId = operationId;
            _activePreviewCts = linkedCts;
        }

        previousCts?.Cancel();
        if (!string.IsNullOrWhiteSpace(previousOperationId))
        {
            try
            {
                _bridge.SmbCancelOperation(new SmbCancelOperationRequest(previousOperationId));
            }
            catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex))
            {
                // 旧预览可能已经自然结束，取消失败不影响新预览。
            }
        }
    }

    private void ClearActivePreview(
        string operationId,
        CancellationTokenSource linkedCts
    )
    {
        lock (_previewSync)
        {
            if (string.Equals(_activePreviewOperationId, operationId, StringComparison.Ordinal))
            {
                _activePreviewOperationId = null;
                _activePreviewCts = null;
            }
        }

        linkedCts.Dispose();
    }

    private static bool IsCompleteCacheFile(DirectoryItemViewModel item, FileInfo fileInfo)
    {
        if (fileInfo.Length <= 0)
        {
            return false;
        }

        return item.SizeBytes.HasValue && fileInfo.Length == unchecked((long)item.SizeBytes.Value);
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

    private void CleanupPreviewCache(string previewDirectory, CancellationToken cancellationToken)
    {
        if (_cacheManagementService is not null)
        {
            _ = _cacheManagementService.CleanupToMaxBytesAsync(
                DefaultTotalCacheMaxBytes,
                CancellationToken.None
            );
            return;
        }

        if (!System.IO.Directory.Exists(previewDirectory))
        {
            return;
        }

        var files = System.IO.Directory
            .EnumerateFiles(previewDirectory, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .OrderBy(file => file.LastAccessTimeUtc)
            .ToArray();
        var totalBytes = files.Sum(file => SafeLength(file));
        foreach (var file in files)
        {
            if (totalBytes <= DefaultTotalCacheMaxBytes)
            {
                break;
            }

            var length = SafeLength(file);
            DeleteIfExists(file.FullName);
            totalBytes -= length;
        }
    }

    private static long SafeLength(FileInfo file)
    {
        try
        {
            return file.Exists ? file.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
