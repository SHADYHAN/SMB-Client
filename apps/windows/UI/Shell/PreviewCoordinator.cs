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
    private readonly Func<Exception, Task<bool>> _handleSessionIssueAsync;
    private CancellationTokenSource? _previewCancellation;
    private int _previewLoadVersion;

    public PreviewCoordinator(
        IPreviewService previewService,
        FileListViewModel fileList,
        PreviewPaneViewModel preview,
        Func<Exception, Task<bool>> handleSessionIssueAsync
    )
    {
        _previewService = previewService;
        _fileList = fileList;
        _preview = preview;
        _handleSessionIssueAsync = handleSessionIssueAsync;
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

        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = null;

        var previewVersion = Interlocked.Increment(ref _previewLoadVersion);
        if (session is not null && item?.Item is { IsDirectory: false } selected)
        {
            var previewCancellation = new CancellationTokenSource();
            _previewCancellation = previewCancellation;
            try
            {
                _preview.ShowPreviewLoading();
                var info = await _previewService.PlanAsync(session, selected, previewCancellation.Token);
                if (previewVersion == _previewLoadVersion && ReferenceEquals(_fileList.SelectedItem, item))
                {
                    _preview.ShowPreviewInfo(info);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (await _handleSessionIssueAsync(ex))
                {
                    return;
                }

                if (previewVersion == _previewLoadVersion && ReferenceEquals(_fileList.SelectedItem, item))
                {
                    _preview.ShowPreviewUnavailable();
                }
            }
            finally
            {
                if (ReferenceEquals(_previewCancellation, previewCancellation))
                {
                    _previewCancellation = null;
                }

                previewCancellation.Dispose();
            }
        }
    }

    public void Cancel()
    {
        Interlocked.Increment(ref _previewLoadVersion);
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = null;
    }
}
