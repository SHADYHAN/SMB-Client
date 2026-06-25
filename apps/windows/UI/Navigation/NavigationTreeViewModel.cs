using System.Collections.ObjectModel;
using System.Windows.Input;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.UI.Infrastructure;

namespace Rynat.WindowsClient.UI.Navigation;

public sealed class NavigationTreeViewModel : ObservableObject
{
    private NavigationNodeViewModel? _selectedNode;
    private NavigationSidebarTab _activeTab = NavigationSidebarTab.Shares;

    public ObservableCollection<NavigationNodeViewModel> Roots { get; } = new();

    public ObservableCollection<FavoriteLinkViewModel> Favorites { get; } = new();

    public NavigationSidebarTab ActiveTab
    {
        get => _activeTab;
        set
        {
            if (SetProperty(ref _activeTab, value))
            {
                OnPropertyChanged(nameof(IsSharesTabActive));
                OnPropertyChanged(nameof(IsFavoritesTabActive));
            }
        }
    }

    public bool IsSharesTabActive => ActiveTab == NavigationSidebarTab.Shares;

    public bool IsFavoritesTabActive => ActiveTab == NavigationSidebarTab.Favorites;

    public NavigationNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set => SetProperty(ref _selectedNode, value);
    }

    public ICommand ShowSharesCommand { get; set; } = new RelayCommand(() => { });

    public ICommand ShowFavoritesCommand { get; set; } = new RelayCommand(() => { });

    public ICommand AddFavoriteCommand { get; set; } = new RelayCommand(() => { });

    public ICommand RemoveFavoriteCommand { get; set; } = new RelayCommand(_ => { });

    public void LoadShares(IReadOnlyList<ServerShare> shares)
    {
        Roots.Clear();
        foreach (var share in shares)
        {
            Roots.Add(new NavigationNodeViewModel(share.Name, share.Name, "/", true));
        }
    }

    public void ReplaceChildren(
        NavigationNodeViewModel parent,
        IReadOnlyList<RemoteFileItem> directories
    )
    {
        parent.Children.Clear();
        foreach (var directory in directories.Where(item => item.IsDirectory))
        {
            parent.Children.Add(new NavigationNodeViewModel(
                directory.Name,
                directory.Share,
                directory.Path,
                true,
                directory
            ));
        }
    }

    public void ShowShares()
    {
        ActiveTab = NavigationSidebarTab.Shares;
    }

    public void ShowFavorites()
    {
        ActiveTab = NavigationSidebarTab.Favorites;
    }

    public void LoadFavorites(IEnumerable<FavoriteLinkItem> favorites)
    {
        Favorites.Clear();
        foreach (var favorite in favorites)
        {
            Favorites.Add(new FavoriteLinkViewModel(favorite));
        }
    }

    public void UpsertFavorite(FavoriteLinkItem favorite)
    {
        var existing = Favorites.FirstOrDefault(candidate =>
            candidate.Item.ServerHost.Equals(favorite.ServerHost, StringComparison.OrdinalIgnoreCase)
            && candidate.Item.Share.Equals(favorite.Share, StringComparison.OrdinalIgnoreCase)
            && candidate.Item.Path.Equals(favorite.Path, StringComparison.OrdinalIgnoreCase)
        );
        if (existing is not null)
        {
            Favorites.Remove(existing);
        }

        Favorites.Insert(0, new FavoriteLinkViewModel(favorite));
    }

    public void RemoveFavorite(string id)
    {
        var existing = Favorites.FirstOrDefault(candidate => candidate.Item.Id == id);
        if (existing is not null)
        {
            Favorites.Remove(existing);
        }
    }
}
