using System.Collections.ObjectModel;
using System.Windows.Input;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.UI.Infrastructure;

namespace Rynat.WindowsClient.UI.Files;

public sealed class FileListViewModel : ObservableObject
{
    private FileItemViewModel? _selectedItem;
    private string _pathTitle = "未连接";

    public ObservableCollection<FileItemViewModel> Items { get; } = new();

    public string PathTitle
    {
        get => _pathTitle;
        set => SetProperty(ref _pathTitle, value);
    }

    public FileItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public ICommand OpenItemCommand { get; set; } = new RelayCommand(_ => { });

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
