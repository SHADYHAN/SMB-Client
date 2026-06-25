using System.Collections.ObjectModel;
using System.Windows.Input;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.UI.Infrastructure;

namespace Rynat.WindowsClient.UI.Files;

public sealed class FileListViewModel : ObservableObject
{
    private readonly List<FileItemViewModel> _allItems = new();
    private readonly List<FileItemViewModel> _selectedItems = new();
    private IReadOnlyList<RemoteFileItem> _selectedRemoteItems = Array.Empty<RemoteFileItem>();
    private FileItemViewModel? _selectedItem;
    private string _pathTitle = "未连接";
    private string _searchText = string.Empty;
    private bool _isLoading;

    public ObservableCollection<FileItemViewModel> Items { get; } = new();

    public IEnumerable<string> AllNames => _allItems.Select(item => item.Name);

    public IReadOnlyList<FileItemViewModel> SelectedItems => _selectedItems;

    public IReadOnlyList<RemoteFileItem> SelectedRemoteItems => _selectedRemoteItems;

    public bool HasSelection => _selectedRemoteItems.Count > 0;

    public bool HasSingleSelection => _selectedRemoteItems.Count == 1;

    public bool HasWritableSelection => _selectedRemoteItems.Count > 0
        && _selectedRemoteItems.All(item => !item.IsShareRoot);

    public bool HasSingleWritableSelection => _selectedRemoteItems.Count == 1
        && !_selectedRemoteItems[0].IsShareRoot;

    public bool IsShareRootView { get; private set; }

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
                if (_selectedItems.Count == 0)
                {
                    RefreshSelectedRemoteItems();
                }

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

    public ICommand CutCommand { get; set; } = new RelayCommand(_ => { });

    public ICommand CopyFileCommand { get; set; } = new RelayCommand(_ => { });

    public ICommand PasteCommand { get; set; } = new RelayCommand(_ => { });

    public ICommand RefreshCommand { get; set; } = new RelayCommand(_ => { });

    public ICommand CreateFolderCommand { get; set; } = new RelayCommand(_ => { });

    public ICommand DeleteCommand { get; set; } = new RelayCommand(_ => { });

    public ICommand RenameCommand { get; set; } = new RelayCommand(_ => { });

    public void ReplaceSelectedItems(IEnumerable<FileItemViewModel> selectedItems)
    {
        _selectedItems.Clear();
        _selectedItems.AddRange(selectedItems);
        RefreshSelectedRemoteItems();
        RefreshSelectionCommands();
    }

    private void RefreshSelectedRemoteItems()
    {
        _selectedRemoteItems = _selectedItems.Count > 0
            ? _selectedItems.Select(item => item.Item).ToArray()
            : SelectedItem is null
                ? Array.Empty<RemoteFileItem>()
                : new[] { SelectedItem.Item };
    }

    private void RefreshOpenItemCommand()
    {
        RefreshSelectionCommands();
    }

    private void RefreshSelectionCommands()
    {
        if (OpenItemCommand is AsyncRelayCommand asyncCommand)
        {
            asyncCommand.RaiseCanExecuteChanged();
        }
        else if (OpenItemCommand is RelayCommand relayCommand)
        {
            relayCommand.RaiseCanExecuteChanged();
        }

        if (CopyLinkCommand is AsyncRelayCommand copyLinkCommand)
        {
            copyLinkCommand.RaiseCanExecuteChanged();
        }

        if (CutCommand is RelayCommand cutCommand)
        {
            cutCommand.RaiseCanExecuteChanged();
        }

        if (CopyFileCommand is RelayCommand copyFileCommand)
        {
            copyFileCommand.RaiseCanExecuteChanged();
        }

        if (DeleteCommand is AsyncRelayCommand deleteCommand)
        {
            deleteCommand.RaiseCanExecuteChanged();
        }

        if (RenameCommand is AsyncRelayCommand renameCommand)
        {
            renameCommand.RaiseCanExecuteChanged();
        }
    }

    public void ShowDirectory(RemoteDirectory directory)
    {
        IsShareRootView = false;
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

    public void ShowShareRoot(ServerSession session)
    {
        IsShareRootView = true;
        PathTitle = "全部共享";
        SearchText = string.Empty;
        _allItems.Clear();

        foreach (var share in session.Shares.OrderBy(share => share.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            _allItems.Add(new FileItemViewModel(new RemoteFileItem(
                share.Name,
                share.Name,
                "/",
                RemoteFileKind.Directory,
                0,
                null,
                IsShareRoot: true
            )));
        }

        ApplyFilter();
    }

    public void Clear(string title)
    {
        IsShareRootView = false;
        PathTitle = title;
        SearchText = string.Empty;
        _allItems.Clear();
        Items.Clear();
        SelectedItem = null;
        ReplaceSelectedItems(Array.Empty<FileItemViewModel>());
        IsLoading = false;
    }

    public bool ContainsName(string name)
    {
        return _allItems.Any(item => item.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
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

        ReplaceSelectedItems(_selectedItems.Where(Items.Contains).ToArray());
    }
}
