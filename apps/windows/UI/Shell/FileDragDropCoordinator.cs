using System.Windows;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Platform.Dialogs;
using Rynat.WindowsClient.Platform.Shell;
using Rynat.WindowsClient.Services.Directory;
using Rynat.WindowsClient.Services.FileOperations;
using Rynat.WindowsClient.Services.FileTransfers;
using Rynat.WindowsClient.UI.Files;
using Rynat.WindowsClient.UI.Status;

namespace Rynat.WindowsClient.UI.Shell;

public sealed class FileDragDropCoordinator
{
    private readonly IFileTransferService _fileTransferService;
    private readonly IFileOperationService _fileOperationService;
    private readonly IRemoteCopyMoveService _remoteCopyMoveService;
    private readonly IDirectoryService _directoryService;
    private readonly IWindowsShellDragDropService _shellDragDropService;
    private readonly IUserDialogService _userDialogService;
    private readonly FileListViewModel _fileList;
    private readonly StatusBarViewModel _status;
    private readonly Func<Exception, string, string> _userFacingError;

    public FileDragDropCoordinator(
        IFileTransferService fileTransferService,
        IFileOperationService fileOperationService,
        IRemoteCopyMoveService remoteCopyMoveService,
        IDirectoryService directoryService,
        IWindowsShellDragDropService shellDragDropService,
        IUserDialogService userDialogService,
        FileListViewModel fileList,
        StatusBarViewModel status,
        Func<Exception, string, string> userFacingError
    )
    {
        _fileTransferService = fileTransferService;
        _fileOperationService = fileOperationService;
        _remoteCopyMoveService = remoteCopyMoveService;
        _directoryService = directoryService;
        _shellDragDropService = shellDragDropService;
        _userDialogService = userDialogService;
        _fileList = fileList;
        _status = status;
        _userFacingError = userFacingError;
    }

    public async Task StartFileDragAsync(
        ServerSession? session,
        object dragSource,
        FileItemViewModel? item,
        IReadOnlyList<RemoteFileItem>? preservedSelection = null
    )
    {
        if (session is null || item is null)
        {
            return;
        }

        var selectedItems = DragSelectionFor(item, preservedSelection);
        if (selectedItems.Count == 0 || selectedItems.Any(selected => selected.IsShareRoot))
        {
            return;
        }

        try
        {
            var dragPayload = new RemoteDragPayload(selectedItems);
            IReadOnlyList<DragFilePayload> shellFiles = Array.Empty<DragFilePayload>();
            var supportsShellDragOut = _shellDragDropService.CanStartDrag(selectedItems);
            if (supportsShellDragOut)
            {
                var result = await _fileTransferService.CreateDragDownloadPayloadAsync(session, selectedItems);
                if (result.Succeeded)
                {
                    shellFiles = result.Files;
                }
            }

            _status.Message = supportsShellDragOut
                ? "拖到文件夹可移动，按 Ctrl 复制；拖到本地位置可复制。"
                : "拖到目标文件夹可移动，按 Ctrl 复制。";
            var effect = _shellDragDropService.StartDrag(dragSource, dragPayload, shellFiles);
            if (effect == DragDropEffects.None)
            {
                _status.Message = "已取消拖拽。";
            }
        }
        catch (Exception ex)
        {
            _status.Message = _userFacingError(ex, "拖拽失败");
        }
    }

    public DragDropEffects GetRemoteDropEffect(
        RemoteDragPayload? payload,
        string targetShare,
        string targetDirectory,
        bool copyRequested
    )
    {
        if (payload is null || payload.Items.Count == 0)
        {
            return DragDropEffects.None;
        }

        if (payload.Items.Any(item => IsInvalidRemoteDropTarget(item, targetShare, targetDirectory)))
        {
            return DragDropEffects.None;
        }

        return ShouldCopy(payload, targetShare, copyRequested)
            ? DragDropEffects.Copy
            : DragDropEffects.Move;
    }

    public async Task DropRemoteItemsAsync(
        ServerSession? session,
        RemoteDragPayload payload,
        string targetShare,
        string targetDirectory,
        bool copyRequested,
        Func<Task> refreshCurrentDirectoryAsync
    )
    {
        if (session is null)
        {
            return;
        }

        var effect = GetRemoteDropEffect(payload, targetShare, targetDirectory, copyRequested);
        if (effect == DragDropEffects.None)
        {
            _status.Message = "不能拖放到该位置。";
            return;
        }

        try
        {
            var targetDirectoryListing = await _directoryService.ListAsync(session, targetShare, targetDirectory);
            var existingNames = new HashSet<string>(
                targetDirectoryListing.Items.Select(item => item.Name),
                StringComparer.CurrentCultureIgnoreCase
            );
            var conflictNames = payload.Items
                .Select(item => item.Name)
                .Where(existingNames.Contains)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            if (conflictNames.Length > 0 && !_userDialogService.ConfirmOverwrite(conflictNames))
            {
                _status.Message = "已取消拖放。";
                return;
            }

            var copy = effect == DragDropEffects.Copy;
            _status.Message = copy ? "正在复制..." : "正在移动...";

            var completed = 0;
            foreach (var item in payload.Items)
            {
                var replaceExisting = conflictNames.Contains(item.Name, StringComparer.CurrentCultureIgnoreCase);
                var result = copy
                    ? await _remoteCopyMoveService.CopyAsync(
                        session,
                        item,
                        targetShare,
                        targetDirectory,
                        replaceExisting
                    )
                    : await _remoteCopyMoveService.MoveAsync(
                        session,
                        item,
                        targetShare,
                        targetDirectory,
                        replaceExisting
                    );

                if (!result.Succeeded)
                {
                    _status.Message = $"{result.Summary}（已完成 {completed}/{payload.Items.Count} 项）";
                    if (completed > 0)
                    {
                        await refreshCurrentDirectoryAsync();
                    }

                    return;
                }

                completed++;
            }

            _status.Message = copy
                ? RemoteDragSummary("已复制", payload.Items)
                : RemoteDragSummary("已移动", payload.Items);
            await refreshCurrentDirectoryAsync();
        }
        catch (Exception ex)
        {
            _status.Message = _userFacingError(ex, "拖放失败");
        }
    }

    public async Task UploadDroppedFilesAsync(
        ServerSession? session,
        string? currentShare,
        string currentPath,
        IReadOnlyList<string> localPaths,
        Func<Task> refreshCurrentDirectoryAsync
    )
    {
        if (session is null || currentShare is null)
        {
            return;
        }

        var existingNames = new HashSet<string>(
            _fileList.AllNames,
            StringComparer.CurrentCultureIgnoreCase
        );
        var conflicts = localPaths
            .Select(System.IO.Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && existingNames.Contains(name))
            .Cast<string>()
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (conflicts.Length > 0 && !_userDialogService.ConfirmOverwrite(conflicts))
        {
            _status.Message = "已取消上传。";
            return;
        }

        try
        {
            _status.Message = "正在上传...";
            var result = await _fileOperationService.UploadFilesAsync(
                session,
                currentShare,
                currentPath,
                localPaths,
                replaceExisting: conflicts.Length > 0
            );
            _status.Message = result.Summary;
            if (result.Succeeded)
            {
                await refreshCurrentDirectoryAsync();
            }
        }
        catch (Exception ex)
        {
            _status.Message = _userFacingError(ex, "上传失败");
        }
    }

    private IReadOnlyList<RemoteFileItem> DragSelectionFor(
        FileItemViewModel item,
        IReadOnlyList<RemoteFileItem>? preservedSelection
    )
    {
        if (ContainsRemoteItem(preservedSelection, item.Item))
        {
            return preservedSelection!;
        }

        return ContainsRemoteItem(_fileList.SelectedRemoteItems, item.Item)
            ? _fileList.SelectedRemoteItems
            : new[] { item.Item };
    }

    private static bool ContainsRemoteItem(
        IReadOnlyList<RemoteFileItem>? items,
        RemoteFileItem item
    )
    {
        return items?.Any(selected =>
            selected.Share.Equals(item.Share, StringComparison.OrdinalIgnoreCase)
            && NormalizeDirectoryPath(selected.Path).Equals(NormalizeDirectoryPath(item.Path), StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static bool ShouldCopy(RemoteDragPayload payload, string targetShare, bool copyRequested)
    {
        return copyRequested || payload.Items.Any(item => !item.Share.Equals(targetShare, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsInvalidRemoteDropTarget(
        RemoteFileItem item,
        string targetShare,
        string targetDirectory
    )
    {
        var targetPath = JoinRemotePath(targetDirectory, item.Name);
        if (IsSameRemoteTarget(item.Share, item.Path, targetShare, targetPath))
        {
            return true;
        }

        return item.IsDirectory && IsNestedDirectoryTarget(item.Share, item.Path, targetShare, targetPath);
    }

    private static string RemoteDragSummary(string prefix, IReadOnlyList<RemoteFileItem> items)
    {
        return items.Count == 1
            ? $"{prefix} {items[0].Name}。"
            : $"{prefix} {items.Count} 个项目。";
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

    private static string JoinRemotePath(string parentPath, string name)
    {
        var parent = NormalizeDirectoryPath(parentPath);
        return parent == "/" ? "/" + name : parent + "/" + name;
    }

    private static string NormalizeDirectoryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        var normalized = path.Replace('\\', '/').Trim().TrimEnd('/');
        return normalized.StartsWith('/') ? normalized : "/" + normalized;
    }
}
