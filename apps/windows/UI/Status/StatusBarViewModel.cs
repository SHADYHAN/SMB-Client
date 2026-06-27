using Rynat.WindowsClient.UI.Infrastructure;

namespace Rynat.WindowsClient.UI.Status;

public sealed class StatusBarViewModel : ObservableObject
{
    private string _message = "准备就绪";
    private string _detail = string.Empty;
    private bool _isBusy;
    private double _progressValue;

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public string Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    public void BeginTask(string message, string detail = "")
    {
        Message = message;
        Detail = detail;
        ProgressValue = 0;
        IsBusy = true;
    }

    public void ReportTaskProgress(int completed, int total, string currentName)
    {
        var safeTotal = Math.Max(1, total);
        ProgressValue = Math.Clamp((double)completed / safeTotal * 100, 0, 100);
        Detail = string.IsNullOrWhiteSpace(currentName)
            ? $"{completed}/{total}"
            : $"{completed}/{total} · {currentName}";
    }

    public void EndTask(string message)
    {
        Message = message;
        ClearTask();
    }

    public void ClearTask()
    {
        Detail = string.Empty;
        ProgressValue = 0;
        IsBusy = false;
    }
}
