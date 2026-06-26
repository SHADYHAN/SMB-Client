using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.UI.Shell;

namespace Rynat.WindowsClient.UI.Navigation;

public partial class NavigationTreeView : UserControl
{
    private NavigationNodeViewModel? _remoteDropTarget;

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

    private void TreeView_OnDragOver(object sender, DragEventArgs e)
    {
        if (TryGetRemotePayload(e, out var payload)
            && TryGetNavigationDropTarget(e, out var target)
            && FindShellViewModel() is { } shell)
        {
            e.Effects = shell.GetRemoteDropEffect(
                payload,
                target.Share,
                target.Path,
                IsCopyRequested(e)
            );
            SetRemoteDropTarget(e.Effects == DragDropEffects.None ? null : target);
            e.Handled = true;
            return;
        }

        SetRemoteDropTarget(null);
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void TreeView_OnDragLeave(object sender, DragEventArgs e)
    {
        if (!IsMouseWithin((FrameworkElement)sender, e))
        {
            SetRemoteDropTarget(null);
        }
    }

    private async void TreeView_OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!TryGetRemotePayload(e, out var payload)
            || !TryGetNavigationDropTarget(e, out var target)
            || FindShellViewModel() is not { } shell)
        {
            SetRemoteDropTarget(null);
            return;
        }

        SetRemoteDropTarget(null);
        await shell.DropRemoteItemsAsync(
            payload,
            target.Share,
            target.Path,
            IsCopyRequested(e)
        );
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

    private void SetRemoteDropTarget(NavigationNodeViewModel? target)
    {
        if (ReferenceEquals(_remoteDropTarget, target))
        {
            return;
        }

        if (_remoteDropTarget is not null)
        {
            _remoteDropTarget.RemoteDropState = NavigationDropState.None;
        }

        _remoteDropTarget = target;

        if (_remoteDropTarget is not null)
        {
            _remoteDropTarget.RemoteDropState = NavigationDropState.ValidTarget;
        }
    }

    private static bool IsMouseWithin(FrameworkElement element, DragEventArgs e)
    {
        var point = e.GetPosition(element);
        return point.X >= 0
            && point.Y >= 0
            && point.X <= element.ActualWidth
            && point.Y <= element.ActualHeight;
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

    private static bool TryGetRemotePayload(DragEventArgs e, out RemoteDragPayload payload)
    {
        payload = null!;
        if (!e.Data.GetDataPresent(RemoteDragPayload.DataFormat)
            || e.Data.GetData(RemoteDragPayload.DataFormat) is not RemoteDragPayload remotePayload)
        {
            return false;
        }

        payload = remotePayload;
        return true;
    }

    private static bool TryGetNavigationDropTarget(
        DragEventArgs e,
        out NavigationNodeViewModel target
    )
    {
        target = null!;
        var item = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
        if (item?.DataContext is not NavigationNodeViewModel node)
        {
            return false;
        }

        target = node;
        return true;
    }

    private static bool IsCopyRequested(DragEventArgs e)
    {
        return e.KeyStates.HasFlag(DragDropKeyStates.ControlKey);
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
