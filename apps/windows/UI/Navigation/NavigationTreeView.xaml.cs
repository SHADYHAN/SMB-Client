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

        if (FindShellViewModel() is { } shell)
        {
            await shell.SelectNavigationNodeAsync(node);
        }
    }

    private async void TreeView_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
        if (item?.DataContext is not NavigationNodeViewModel node)
        {
            return;
        }

        item.IsSelected = true;
        if (FindShellViewModel() is { } shell)
        {
            await shell.ToggleNavigationNodeAsync(node);
            e.Handled = true;
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
