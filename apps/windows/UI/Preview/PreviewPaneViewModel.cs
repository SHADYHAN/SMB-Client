using System.Windows.Input;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.UI.Infrastructure;

namespace Rynat.WindowsClient.UI.Preview;

public sealed class PreviewPaneViewModel : ObservableObject
{
    private string _title = "预览";
    private string _subtitle = "选择一个文件查看信息";
    private string _contentType = "";
    private string? _message;
    private string? _localImagePath;
    private string? _localVideoPath;
    private bool _isVideoPlaying;
    private bool _isLoading;
    private RemoteFileItem? _selectedItem;
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

    public string? Message
    {
        get => _message;
        set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(PreviewText));
            }
        }
    }

    public string? LocalImagePath
    {
        get => _localImagePath;
        set
        {
            if (SetProperty(ref _localImagePath, value))
            {
                OnPropertyChanged(nameof(LocalImageUri));
                OnPropertyChanged(nameof(HasImagePreview));
                OnPropertyChanged(nameof(HasPreviewMedia));
                OnPropertyChanged(nameof(ShouldShowImagePreview));
                OnPropertyChanged(nameof(ShouldShowVideoPreview));
                OnPropertyChanged(nameof(PreviewText));
            }
        }
    }

    public string? LocalVideoPath
    {
        get => _localVideoPath;
        set
        {
            if (SetProperty(ref _localVideoPath, value))
            {
                OnPropertyChanged(nameof(LocalVideoUri));
                OnPropertyChanged(nameof(HasVideoPreview));
                OnPropertyChanged(nameof(HasPreviewMedia));
                OnPropertyChanged(nameof(ShouldShowImagePreview));
                OnPropertyChanged(nameof(ShouldShowVideoPreview));
                OnPropertyChanged(nameof(PreviewText));
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(PreviewText));
            }
        }
    }

    public Uri? LocalImageUri => BuildLocalUri(LocalImagePath);

    public Uri? LocalVideoUri => BuildLocalUri(LocalVideoPath);

    public bool HasImagePreview => !string.IsNullOrWhiteSpace(LocalImagePath);

    public bool HasVideoPreview => !string.IsNullOrWhiteSpace(LocalVideoPath);

    public bool HasPreviewMedia => ShouldShowImagePreview || ShouldShowVideoPreview;

    public bool ShouldShowImagePreview => HasImagePreview && (!HasVideoPreview || !IsVideoPlaying);

    public bool ShouldShowVideoPreview => HasVideoPreview && IsVideoPlaying;

    public bool IsVideoPlaying
    {
        get => _isVideoPlaying;
        set
        {
            if (SetProperty(ref _isVideoPlaying, value))
            {
                OnPropertyChanged(nameof(ShouldShowImagePreview));
                OnPropertyChanged(nameof(ShouldShowVideoPreview));
                OnPropertyChanged(nameof(HasPreviewMedia));
                OnPropertyChanged(nameof(PreviewText));
            }
        }
    }

    public string PreviewText
    {
        get
        {
            if (IsLoading)
            {
                return "正在加载预览...";
            }

            if (HasPreviewMedia)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(Message))
            {
                return Message;
            }

            if (SelectedItem is null)
            {
                return "暂无预览";
            }

            return string.IsNullOrWhiteSpace(ContentType) ? "暂无预览" : ContentType;
        }
    }

    public RemoteFileItem? SelectedItem
    {
        get => _selectedItem;
        private set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                RefreshCopyLinkCommand();
            }
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public ICommand ToggleCommand { get; set; } = new RelayCommand(() => { });

    public ICommand CopyLinkCommand { get; set; } = new RelayCommand(_ => { });

    public void ShowSelection(RemoteFileItem? item)
    {
        SelectedItem = item;
        LocalImagePath = null;
        LocalVideoPath = null;
        IsVideoPlaying = false;
        Message = null;
        IsLoading = false;
        if (item is null)
        {
            Title = "预览";
            Subtitle = "选择一个文件查看信息";
            ContentType = "";
            OnPropertyChanged(nameof(PreviewText));
            return;
        }

        Title = item.Name;
        Subtitle = item.IsDirectory ? "文件夹" : $"{FormatSize(item.Size)} · {item.ModifiedAt?.LocalDateTime:yyyy-MM-dd HH:mm}";
        ContentType = "";
        Message = null;
        OnPropertyChanged(nameof(PreviewText));
    }

    public void ShowPreviewLoading()
    {
        LocalImagePath = null;
        LocalVideoPath = null;
        IsVideoPlaying = false;
        Message = null;
        IsLoading = true;
    }

    public void ShowPreviewInfo(PreviewInfo info)
    {
        IsLoading = false;
        ContentType = info.ContentType;
        Message = info.Message;
        LocalImagePath = info.LocalImagePath;
        LocalVideoPath = info.LocalVideoPath;
        IsVideoPlaying = false;
        OnPropertyChanged(nameof(PreviewText));
    }

    public void ShowPreviewUnavailable()
    {
        IsLoading = false;
        LocalImagePath = null;
        LocalVideoPath = null;
        IsVideoPlaying = false;
        ContentType = "";
        Message = null;
        OnPropertyChanged(nameof(PreviewText));
    }

    private static Uri? BuildLocalUri(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : Uri.TryCreate(path, UriKind.Absolute, out var uri)
                ? uri
                : null;
    }

    private void RefreshCopyLinkCommand()
    {
        if (CopyLinkCommand is AsyncRelayCommand asyncCommand)
        {
            asyncCommand.RaiseCanExecuteChanged();
        }
        else if (CopyLinkCommand is RelayCommand relayCommand)
        {
            relayCommand.RaiseCanExecuteChanged();
        }
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
