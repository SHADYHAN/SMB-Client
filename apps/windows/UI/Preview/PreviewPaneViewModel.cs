using System.Windows.Input;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.UI.Infrastructure;

namespace Rynat.WindowsClient.UI.Preview;

public sealed class PreviewPaneViewModel : ObservableObject
{
    private string _title = "预览";
    private string _subtitle = "选择一个文件查看信息";
    private string _contentType = "";
    private bool _isVisible = true;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        set => SetProperty(ref _subtitle, value);
    }

    public string ContentType
    {
        get => _contentType;
        set => SetProperty(ref _contentType, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public ICommand ToggleCommand { get; set; } = new RelayCommand(() => { });

    public void ShowSelection(RemoteFileItem? item)
    {
        if (item is null)
        {
            Title = "预览";
            Subtitle = "选择一个文件查看信息";
            ContentType = "";
            return;
        }

        Title = item.Name;
        Subtitle = item.IsDirectory ? "文件夹" : $"{FormatSize(item.Size)} · {item.ModifiedAt?.LocalDateTime:yyyy-MM-dd HH:mm}";
        ContentType = "";
    }

    public void ShowPreviewInfo(PreviewInfo info)
    {
        ContentType = info.ContentType;
    }

    private static string FormatSize(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }
}
