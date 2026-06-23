using System.Windows;
using System.Windows.Controls;
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
