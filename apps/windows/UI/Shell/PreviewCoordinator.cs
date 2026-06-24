using System.Threading;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Services.Preview;
using Rynat.WindowsClient.UI.Files;
using Rynat.WindowsClient.UI.Preview;

namespace Rynat.WindowsClient.UI.Shell;

public sealed class PreviewCoordinator
{
    private readonly IPreviewService _previewService;
    private readonly FileListViewModel _fileList;
    private readonly PreviewPaneViewModel _preview;
    private int _previewLoadVersion;

    public PreviewCoordinator(
        IPreviewService previewService,
        FileListViewModel fileList,
        PreviewPaneViewModel preview
    )
    {
        _previewService = previewService;
        _fileList = fileList;
        _preview = preview;
    }

    public async Task SelectFileAsync(
        ServerSession? session,
        FileItemViewModel? item,
        Action refreshCommands
    )
    {
        _fileList.SelectedItem = item;
        _preview.ShowSelection(item?.Item);
        refreshCommands();

        var previewVersion = Interlocked.Increment(ref _previewLoadVersion);
        if (session is not null && item?.Item is { IsDirectory: false } selected)
        {
            try
            {
                _preview.ShowPreviewLoading();
                var info = await _previewService.PlanAsync(session, selected);
                if (previewVersion == _previewLoadVersion && ReferenceEquals(_fileList.SelectedItem, item))
                {
                    _preview.ShowPreviewInfo(info);
                }
            }
            catch
            {
                if (previewVersion == _previewLoadVersion && ReferenceEquals(_fileList.SelectedItem, item))
                {
                    _preview.ShowPreviewUnavailable();
                }
            }
        }
    }
}
