using Rynat.WindowsClient.UI.Infrastructure;

namespace Rynat.WindowsClient.UI.Status;

public sealed class StatusBarViewModel : ObservableObject
{
    private string _message = "准备就绪";

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }
}
