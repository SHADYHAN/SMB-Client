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
        Clipboard = new RemoteClipboardItem(RemoteClipboardMode.Cut, item);
        return $"已剪切 {item.Name}。";
    }

    public string Copy(RemoteFileItem item)
    {
        Clipboard = new RemoteClipboardItem(RemoteClipboardMode.Copy, item);
        return $"已复制 {item.Name}。";
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

        var targetPath = JoinRemotePath(targetDirectory, clipboard.Item.Name);
        if (IsSameRemoteTarget(clipboard.Item.Share, clipboard.Item.Path, targetShare, targetPath))
        {
            var summary = clipboard.Mode == RemoteClipboardMode.Cut
                ? "项目已在当前位置。"
                : "不能复制到原位置。";
            return StatusOnly(summary, "file.same_target");
        }
        if (clipboard.Item.IsDirectory && IsNestedDirectoryTarget(clipboard.Item.Share, clipboard.Item.Path, targetShare, targetPath))
        {
            var summary = clipboard.Mode == RemoteClipboardMode.Cut
                ? "不能移动到自身内部。"
                : "不能复制到自身内部。";
            return StatusOnly(summary, "file.nested_target");
        }

        var replaceExisting = targetContainsName(clipboard.Item.Name);
        if (replaceExisting && !_confirmOverwrite(new[] { clipboard.Item.Name }))
        {
            return StatusOnly("已取消粘贴。", "file.cancelled");
        }

        var result = clipboard.Mode == RemoteClipboardMode.Cut
            ? await _remoteCopyMoveService.MoveAsync(
                session,
                clipboard.Item,
                targetShare,
                targetDirectory,
                replaceExisting,
                cancellationToken
            )
            : await _remoteCopyMoveService.CopyAsync(
                session,
                clipboard.Item,
                targetShare,
                targetDirectory,
                replaceExisting,
                cancellationToken
            );

        return new RemoteClipboardPasteResult(
            result,
            ClearClipboard: result.Succeeded && clipboard.Mode == RemoteClipboardMode.Cut
        );
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
