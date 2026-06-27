using System.Windows;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Platform.Dialogs;
using Rynat.WindowsClient.Platform.Shell;
using Rynat.WindowsClient.Services.FileOperations;
using Rynat.WindowsClient.Services.FileTransfers;
using Rynat.WindowsClient.UI.Files;
using Rynat.WindowsClient.UI.Status;

namespace Rynat.WindowsClient.UI.Shell;

public sealed class FileDragDropCoordinator
{
    private readonly IFileTransferService _fileTransferService;
    private readonly IFileOperationService _fileOperationService;
    private readonly IWindowsShellDragDropService _shellDragDropService;
    private readonly IUserDialogService _userDialogService;
    private readonly FileListViewModel _fileList;
    private readonly StatusBarViewModel _status;
    private readonly Func<Exception, string, string> _userFacingError;
    private readonly Func<Exception, Task<bool>> _handleSessionIssueAsync;
    private readonly Func<FileOperationResult, Task<bool>> _handleOperationResultAsync;

    public FileDragDropCoordinator(
        IFileTransferService fileTransferService,
        IFileOperationService fileOperationService,
        IWindowsShellDragDropService shellDragDropService,
        IUserDialogService userDialogService,
        FileListViewModel fileList,
        StatusBarViewModel status,
        Func<Exception, string, string> userFacingError,
        Func<Exception, Task<bool>> handleSessionIssueAsync,
        Func<FileOperationResult, Task<bool>> handleOperationResultAsync
    )
    {
        _fileTransferService = fileTransferService;
        _fileOperationService = fileOperationService;
        _shellDragDropService = shellDragDropService;
        _userDialogService = userDialogService;
        _fileList = fileList;
        _status = status;
        _userFacingError = userFacingError;
        _handleSessionIssueAsync = handleSessionIssueAsync;
        _handleOperationResultAsync = handleOperationResultAsync;
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
                _status.Message = "正在准备拖出文件...";
                var result = await _fileTransferService.CreateDragDownloadPayloadAsync(session, selectedItems);
                if (result.Succeeded)
                {
                    shellFiles = result.Files;
                }
                else
                {
                    _status.Message = result.Summary;
                    return;
                }
            }

            _status.Message = supportsShellDragOut
                ? "拖到本地位置可复制。"
                : "该项目暂不支持拖出，请使用“下载到...”。";
            var effect = _shellDragDropService.StartDrag(dragSource, dragPayload, shellFiles);
            if (effect == DragDropEffects.None)
            {
                _status.Message = "已取消拖拽。";
            }
        }
        catch (Exception ex)
        {
            if (await _handleSessionIssueAsync(ex))
            {
                return;
            }

            _status.Message = _userFacingError(ex, "拖拽失败");
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
            _status.BeginTask("正在上传...", localPaths.Count == 1 ? System.IO.Path.GetFileName(localPaths[0]) : $"0/{localPaths.Count}");
            var progress = new Progress<FileBatchProgress>(item =>
                _status.ReportTaskProgress(item.Completed, item.Total, item.CurrentName)
            );
            var result = await _fileOperationService.UploadFilesAsync(
                session,
                currentShare,
                currentPath,
                localPaths,
                replaceExisting: conflicts.Length > 0,
                progress
            );
            if (await _handleOperationResultAsync(result))
            {
                return;
            }

            _status.EndTask(result.Summary);
            if (result.Succeeded)
            {
                await refreshCurrentDirectoryAsync();
            }
        }
        catch (Exception ex)
        {
            if (await _handleSessionIssueAsync(ex))
            {
                return;
            }

            _status.EndTask(_userFacingError(ex, "上传失败"));
        }
        finally
        {
            if (_status.IsBusy)
            {
                _status.ClearTask();
            }
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
