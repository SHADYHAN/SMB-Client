using System.Collections.ObjectModel;
using System.Windows.Input;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.UI.Infrastructure;

namespace Rynat.WindowsClient.UI.Files;

public sealed class FileListViewModel : ObservableObject
{
    private FileItemViewModel? _selectedItem;
    private string _pathTitle = "未连接";
    private bool _isLoading;

    public ObservableCollection<FileItemViewModel> Items { get; } = new();

    public string PathTitle
    {
        get => _pathTitle;
        set => SetProperty(ref _pathTitle, value);
    }

    public FileItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                RefreshOpenItemCommand();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public ICommand OpenItemCommand { get; set; } = new RelayCommand(_ => { });

    public ICommand CopyLinkCommand { get; set; } = new RelayCommand(_ => { });

    public ICommand RefreshCommand { get; set; } = new RelayCommand(_ => { });

    public ICommand CreateFolderCommand { get; set; } = new RelayCommand(_ => { });

    public ICommand DeleteCommand { get; set; } = new RelayCommand(_ => { });

    private void RefreshOpenItemCommand()
    {
        if (OpenItemCommand is AsyncRelayCommand asyncCommand)
        {
            asyncCommand.RaiseCanExecuteChanged();
        }
        else if (OpenItemCommand is RelayCommand relayCommand)
        {
            relayCommand.RaiseCanExecuteChanged();
        }
    }

    public void ShowDirectory(RemoteDirectory directory)
    {
        PathTitle = directory.Path == "/"
            ? directory.Share
            : $"{directory.Share}{directory.Path}";
        Items.Clear();

        foreach (var item in directory.Items
            .OrderByDescending(item => item.IsDirectory)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Items.Add(new FileItemViewModel(item));
        }
    }
}
