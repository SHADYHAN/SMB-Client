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

    private async void TreeView_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
        if (item?.DataContext is not NavigationNodeViewModel node)
        {
            return;
        }

        item.IsSelected = true;
        e.Handled = true;

        var shell = FindShellViewModel();
        if (shell is null)
        {
            return;
        }

        try
        {
            await shell.ToggleNavigationNodeAsync(node);
        }
        catch (Exception ex)
        {
            shell.ReportUiError(ex, "目录打开失败");
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
