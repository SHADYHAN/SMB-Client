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

    public FileDragDropCoordinator(
        IFileTransferService fileTransferService,
        IFileOperationService fileOperationService,
        IWindowsShellDragDropService shellDragDropService,
        IUserDialogService userDialogService,
        FileListViewModel fileList,
        StatusBarViewModel status,
        Func<Exception, string, string> userFacingError
    )
    {
        _fileTransferService = fileTransferService;
        _fileOperationService = fileOperationService;
        _shellDragDropService = shellDragDropService;
        _userDialogService = userDialogService;
        _fileList = fileList;
        _status = status;
        _userFacingError = userFacingError;
    }

    public async Task StartFileDragAsync(
        ServerSession? session,
        object dragSource,
        FileItemViewModel? item
    )
    {
        if (session is null || item is null)
        {
            return;
        }

        var selectedItems = new[] { item.Item };

        if (!_shellDragDropService.CanStartDrag(selectedItems))
        {
            _status.Message = item.Item.IsDirectory ? "暂不支持拖出文件夹。" : "无法拖出。";
            return;
        }

        try
        {
            _status.Message = "拖到本地位置后开始复制。";
            var result = await _fileTransferService.CreateDragDownloadPayloadAsync(session, selectedItems);
            if (!result.Succeeded)
            {
                _status.Message = result.Summary;
                return;
            }

            var completed = _shellDragDropService.StartDrag(dragSource, result.Files);
            _status.Message = completed ? "拖出完成。" : "已取消拖出。";
        }
        catch (Exception ex)
        {
            _status.Message = _userFacingError(ex, "拖出失败");
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
}
