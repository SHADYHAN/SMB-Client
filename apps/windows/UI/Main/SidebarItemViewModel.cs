using Rynat.Client;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Rynat.WindowsClient.UI.Main;

public enum SidebarItemKind
{
    Share,
    Directory,
    Favorite
}

public sealed class SidebarItemViewModel : INotifyPropertyChanged
{
    private static readonly SolidColorBrush SelectedBrush = new(ColorHelper.FromArgb(255, 212, 230, 255));
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);
    private static readonly SolidColorBrush FavoriteAccentBrush = new(ColorHelper.FromArgb(255, 216, 178, 20));
    private static readonly SolidColorBrush DirectoryAccentBrush = new(ColorHelper.FromArgb(255, 36, 148, 105));

    private bool _isExpanded;
    private bool _canExpand;
    private bool _isSelected;

    private SidebarItemViewModel(
        SidebarItemKind kind,
        string title,
        string subtitle,
        string displayPath,
        string iconGlyph,
        int depth,
        bool isExpanded,
        bool canExpand,
        ShareListItem? share,
        DirectoryItemViewModel? directoryItem,
        QuickLink? quickLink
    )
    {
        Kind = kind;
        Title = title;
        Subtitle = subtitle;
        DisplayPath = displayPath;
        IconGlyph = iconGlyph;
        Depth = depth;
        _isExpanded = isExpanded;
        _canExpand = canExpand;
        Share = share;
        DirectoryItem = directoryItem;
        QuickLink = quickLink;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SidebarItemKind Kind { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string DisplayPath { get; }

    public string IconGlyph { get; }

    public int Depth { get; }

    public Thickness RowPadding => new(10 + Math.Min(Depth, 6) * 14, 0, 8, 0);

    public bool IsExpanded
    {
        get => _isExpanded;
        private set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExpandGlyph));
        }
    }

    public bool CanExpand
    {
        get => _canExpand;
        private set
        {
            if (_canExpand == value)
            {
                return;
            }

            _canExpand = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExpandGlyph));
        }
    }

    public string ExpandGlyph => CanExpand
        ? IsExpanded ? "\uE70D" : "\uE76C"
        : string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        private set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionBrush));
        }
    }

    public Brush SelectionBrush => IsSelected
        ? SelectedBrush
        : TransparentBrush;

    public Brush AccentBrush => IsFavorite
        ? FavoriteAccentBrush
        : DirectoryAccentBrush;

    public ShareListItem? Share { get; }

    public DirectoryItemViewModel? DirectoryItem { get; }

    public QuickLink? QuickLink { get; }

    public bool IsShare => Kind == SidebarItemKind.Share;

    public bool IsDirectory => Kind is SidebarItemKind.Share or SidebarItemKind.Directory;

    public bool IsFavorite => Kind == SidebarItemKind.Favorite;

    public string? LinkId => QuickLink?.Id;

    public void SetExpansion(bool isExpanded, bool? canExpand = null)
    {
        if (canExpand.HasValue)
        {
            CanExpand = canExpand.Value;
        }

        IsExpanded = isExpanded;
    }

    public void SetSelected(bool isSelected)
    {
        IsSelected = isSelected;
    }

    public static SidebarItemViewModel FromShare(
        ShareListItem share,
        bool isExpanded = false,
        bool canExpand = true
    ) =>
        new(
            SidebarItemKind.Share,
            share.Name,
            share.Comment,
            "/" + share.Name,
            "\uE8B7",
            0,
            isExpanded,
            canExpand,
            share,
            null,
            null
        );

    public static SidebarItemViewModel FromDirectory(
        DirectoryItemViewModel item,
        int depth,
        bool isExpanded,
        bool canExpand
    ) =>
        new(
            SidebarItemKind.Directory,
            item.Name,
            item.DisplayPath,
            item.DisplayPath,
            "\uE8B7",
            depth,
            isExpanded,
            canExpand,
            null,
            item,
            null
        );

    public static SidebarItemViewModel FromQuickLink(QuickLink link)
    {
        var displayPath = BuildDisplayPath(link.Target);
        return new(
            SidebarItemKind.Favorite,
            BuildTitle(link.Target),
            displayPath,
            displayPath,
            "\uE734",
            0,
            false,
            false,
            null,
            null,
            link
        );
    }

    private static string BuildTitle(QuickLinkTarget target)
    {
        var name = target.Name?.Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var trimmedPath = (target.Path ?? string.Empty).Trim('/');
        if (!string.IsNullOrWhiteSpace(trimmedPath))
        {
            var parts = trimmedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                return parts[^1];
            }
        }

        return target.Share;
    }

    private static string BuildDisplayPath(QuickLinkTarget target)
    {
        var trimmedPath = (target.Path ?? string.Empty).Trim('/');
        return string.IsNullOrWhiteSpace(trimmedPath)
            ? "/" + target.Share
            : "/" + target.Share + "/" + trimmedPath;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
