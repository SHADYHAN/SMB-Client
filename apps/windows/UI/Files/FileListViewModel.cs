using System.Collections.ObjectModel;
using System.Windows.Input;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.UI.Infrastructure;

namespace Rynat.WindowsClient.UI.Files;

public sealed class FileListViewModel : ObservableObject
{
    private readonly List<FileItemViewModel> _allItems = new();
    private FileItemViewModel? _selectedItem;
    private string _pathTitle = "未连接";
    private string _searchText = string.Empty;
    private bool _isLoading;

    public ObservableCollection<FileItemViewModel> Items { get; } = new();

    public string PathTitle
    {
        get => _pathTitle;
        set => SetProperty(ref _pathTitle, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
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

    public ICommand RenameCommand { get; set; } = new RelayCommand(_ => { });

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
        SearchText = string.Empty;
        _allItems.Clear();

        foreach (var item in directory.Items
            .OrderByDescending(item => item.IsDirectory)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            _allItems.Add(new FileItemViewModel(item));
        }

        ApplyFilter();
    }

    public void Clear(string title)
    {
        PathTitle = title;
        SearchText = string.Empty;
        _allItems.Clear();
        Items.Clear();
        SelectedItem = null;
        IsLoading = false;
    }

    private void ApplyFilter()
    {
        var selected = SelectedItem;
        var query = SearchText.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allItems
            : _allItems.Where(item => item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));

        Items.Clear();
        foreach (var item in filtered)
        {
            Items.Add(item);
        }

        if (selected is null || !Items.Contains(selected))
        {
            SelectedItem = null;
        }
    }
}
