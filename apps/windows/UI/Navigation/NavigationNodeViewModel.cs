using System.Collections.ObjectModel;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.UI.Infrastructure;

namespace Rynat.WindowsClient.UI.Navigation;

public sealed class NavigationNodeViewModel : ObservableObject
{
    private bool _isExpanded;
    private bool _isSelected;

    public NavigationNodeViewModel(
        string title,
        string share,
        string path,
        bool canExpand,
        RemoteFileItem? item = null
    )
    {
        Title = title;
        Share = share;
        Path = path;
        CanExpand = canExpand;
        Item = item;
    }

    public string Title { get; }

    public string Share { get; }

    public string Path { get; }

    public bool CanExpand { get; }

    public RemoteFileItem? Item { get; }

    public ObservableCollection<NavigationNodeViewModel> Children { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
