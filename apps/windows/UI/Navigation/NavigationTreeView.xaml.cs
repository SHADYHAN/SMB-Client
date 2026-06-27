using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Rynat.WindowsClient.UI.Shell;

namespace Rynat.WindowsClient.UI.Navigation;

public partial class NavigationTreeView : UserControl
{
    public NavigationTreeView()
    {
        InitializeComponent();
    }

    private async void TreeView_OnSelectedItemChanged(
        object sender,
        System.Windows.RoutedPropertyChangedEventArgs<object> e
    )
    {
        if (e.NewValue is not NavigationNodeViewModel node)
        {
            return;
        }

        var shell = FindShellViewModel();
        if (shell is null)
        {
            return;
        }

        try
        {
            await shell.SelectNavigationNodeAsync(node);
        }
        catch (Exception ex)
        {
            shell.ReportUiError(ex, "目录打开失败");
        }
    }

    private void TreeView_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
        if (item?.DataContext is not NavigationNodeViewModel node)
        {
            return;
        }

        item.IsSelected = true;
        e.Handled = true;

        node.IsExpanded = !node.IsExpanded;
    }

    private async void FavoritesList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as ListView)?.SelectedItem is not FavoriteLinkViewModel favorite)
        {
            return;
        }

        await OpenFavoriteAsync(favorite);
    }

    private async void FavoritesList_OnKeyDown(object sender, KeyEventArgs e)
    {
        if ((sender as ListView)?.SelectedItem is not FavoriteLinkViewModel favorite)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                await OpenFavoriteAsync(favorite);
                e.Handled = true;
                break;
            case Key.Delete when DataContext is NavigationTreeViewModel viewModel
                && viewModel.RemoveFavoriteCommand.CanExecute(favorite):
                viewModel.RemoveFavoriteCommand.Execute(favorite);
                e.Handled = true;
                break;
        }
    }

    private async void FavoriteOpenMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (FindAncestor<ContextMenu>((DependencyObject)e.OriginalSource)
                is not { PlacementTarget: ListView { SelectedItem: FavoriteLinkViewModel favorite } })
        {
            return;
        }

        await OpenFavoriteAsync(favorite);
    }

    private async Task OpenFavoriteAsync(FavoriteLinkViewModel favorite)
    {
        var shell = FindShellViewModel();
        if (shell is null)
        {
            return;
        }

        try
        {
            await shell.OpenFavoriteAsync(favorite);
        }
        catch (Exception ex)
        {
            shell.ReportUiError(ex, "收藏打开失败");
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private ShellViewModel? FindShellViewModel()
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: ShellViewModel shell })
            {
                return shell;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
