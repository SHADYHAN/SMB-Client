using System.Collections.ObjectModel;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.UI.Infrastructure;

namespace Rynat.WindowsClient.UI.Navigation;

public sealed class NavigationTreeViewModel : ObservableObject
{
    private NavigationNodeViewModel? _selectedNode;

    public ObservableCollection<NavigationNodeViewModel> Roots { get; } = new();

    public NavigationNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set => SetProperty(ref _selectedNode, value);
    }

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
}
