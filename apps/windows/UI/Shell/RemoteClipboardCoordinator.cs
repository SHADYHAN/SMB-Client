using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Services.FileOperations;

namespace Rynat.WindowsClient.UI.Shell;

public sealed class RemoteClipboardCoordinator
{
    private readonly IRemoteCopyMoveService _remoteCopyMoveService;
    private readonly Func<IReadOnlyList<string>, bool> _confirmOverwrite;

    public RemoteClipboardCoordinator(
        IRemoteCopyMoveService remoteCopyMoveService,
        Func<IReadOnlyList<string>, bool> confirmOverwrite
    )
    {
        _remoteCopyMoveService = remoteCopyMoveService;
        _confirmOverwrite = confirmOverwrite;
    }

    public RemoteClipboardItem? Clipboard { get; private set; }

    public bool CanPaste => Clipboard is not null;

    public string Cut(RemoteFileItem item)
    {
        return Cut(new[] { item });
    }

    public string Cut(IReadOnlyList<RemoteFileItem> items)
    {
        if (items.Count == 0)
        {
            Clear();
            return "没有可剪切的项目。";
        }

        Clipboard = new RemoteClipboardItem(RemoteClipboardMode.Cut, items);
        return ClipboardSummary("已剪切", items);
    }

    public string Copy(RemoteFileItem item)
    {
        return Copy(new[] { item });
    }

    public string Copy(IReadOnlyList<RemoteFileItem> items)
    {
        if (items.Count == 0)
        {
            Clear();
            return "没有可复制的项目。";
        }

        Clipboard = new RemoteClipboardItem(RemoteClipboardMode.Copy, items);
        return ClipboardSummary("已复制", items);
    }

    public void Clear()
    {
        Clipboard = null;
    }

    public async Task<RemoteClipboardPasteResult?> PasteAsync(
        ServerSession session,
        string targetShare,
        string targetDirectory,
        Func<string, bool> targetContainsName,
        CancellationToken cancellationToken = default
    )
    {
        if (Clipboard is not { } clipboard)
        {
            return null;
        }

        if (clipboard.Items.Count == 0)
        {
            return null;
        }

        var sameTarget = clipboard.Items.FirstOrDefault(item =>
            IsSameRemoteTarget(item.Share, item.Path, targetShare, JoinRemotePath(targetDirectory, item.Name))
        );
        if (sameTarget is not null)
        {
            var sameTargetSummary = clipboard.Mode == RemoteClipboardMode.Cut
                ? SameTargetSummary("项目已在当前位置。", "部分项目已在当前位置", sameTarget, clipboard.Items)
                : SameTargetSummary("不能复制到原位置。", "部分项目不能复制到原位置", sameTarget, clipboard.Items);
            return StatusOnly(sameTargetSummary, "file.same_target");
        }

        var nestedTarget = clipboard.Items.FirstOrDefault(item =>
            item.IsDirectory && IsNestedDirectoryTarget(item.Share, item.Path, targetShare, JoinRemotePath(targetDirectory, item.Name))
        );
        if (nestedTarget is not null)
        {
            var nestedTargetSummary = clipboard.Mode == RemoteClipboardMode.Cut
                ? $"不能移动到自身内部：{nestedTarget.Name}。"
                : $"不能复制到自身内部：{nestedTarget.Name}。";
            return StatusOnly(nestedTargetSummary, "file.nested_target");
        }

        var conflictNames = clipboard.Items
            .Select(item => item.Name)
            .Where(targetContainsName)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (conflictNames.Length > 0 && !_confirmOverwrite(conflictNames))
        {
            return StatusOnly("已取消粘贴。", "file.cancelled");
        }

        var completed = 0;
        foreach (var item in clipboard.Items)
        {
            var replaceExisting = conflictNames.Contains(item.Name, StringComparer.CurrentCultureIgnoreCase);
            var result = clipboard.Mode == RemoteClipboardMode.Cut
                ? await _remoteCopyMoveService.MoveAsync(
                    session,
                    item,
                    targetShare,
                    targetDirectory,
                    replaceExisting,
                    cancellationToken
                )
                : await _remoteCopyMoveService.CopyAsync(
                    session,
                    item,
                    targetShare,
                    targetDirectory,
                    replaceExisting,
                    cancellationToken
                );

            if (!result.Succeeded)
            {
                return new RemoteClipboardPasteResult(
                    new FileOperationResult(false, $"{result.Summary}（已完成 {completed}/{clipboard.Items.Count} 项）", result.ErrorCode),
                    ClearClipboard: false
                );
            }

            completed++;
        }

        var summary = clipboard.Mode == RemoteClipboardMode.Cut
            ? PasteSummary("已移动", clipboard.Items)
            : PasteSummary("已复制", clipboard.Items);

        return new RemoteClipboardPasteResult(
            new FileOperationResult(true, summary),
            ClearClipboard: clipboard.Mode == RemoteClipboardMode.Cut
        );
    }

    private static string ClipboardSummary(string prefix, IReadOnlyList<RemoteFileItem> items)
    {
        return items.Count == 1
            ? $"{prefix} {items[0].Name}。"
            : $"{prefix} {items.Count} 个项目。";
    }

    private static string PasteSummary(string prefix, IReadOnlyList<RemoteFileItem> items)
    {
        return items.Count == 1
            ? $"{prefix} {items[0].Name}。"
            : $"{prefix} {items.Count} 个项目。";
    }

    private static string SameTargetSummary(
        string singleItemSummary,
        string multipleItemPrefix,
        RemoteFileItem item,
        IReadOnlyList<RemoteFileItem> items
    )
    {
        return items.Count == 1
            ? singleItemSummary
            : $"{multipleItemPrefix}：{item.Name}。";
    }

    private static RemoteClipboardPasteResult StatusOnly(string summary, string errorCode)
    {
        return new RemoteClipboardPasteResult(
            new FileOperationResult(false, summary, errorCode),
            ClearClipboard: false
        );
    }

    private static string JoinRemotePath(string parentPath, string name)
    {
        var parent = NormalizeDirectoryPath(parentPath);
        return parent == "/" ? "/" + name : parent + "/" + name;
    }

    private static bool IsSameRemoteTarget(string leftShare, string leftPath, string rightShare, string rightPath)
    {
        return leftShare.Equals(rightShare, StringComparison.OrdinalIgnoreCase)
            && NormalizeDirectoryPath(leftPath).Equals(NormalizeDirectoryPath(rightPath), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNestedDirectoryTarget(string sourceShare, string sourcePath, string targetShare, string targetPath)
    {
        if (!sourceShare.Equals(targetShare, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var source = NormalizeDirectoryPath(sourcePath).TrimEnd('/');
        var target = NormalizeDirectoryPath(targetPath).TrimEnd('/');
        return target.StartsWith(source + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectoryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        var normalized = path.Replace('\\', '/').Trim();
        return normalized.StartsWith('/') ? normalized : "/" + normalized;
    }
}
