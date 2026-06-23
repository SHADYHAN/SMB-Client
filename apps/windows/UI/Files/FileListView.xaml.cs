using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Rynat.WindowsClient.UI.Shell;

namespace Rynat.WindowsClient.UI.Files;

public partial class FileListView : UserControl
{
    public FileListView()
    {
        InitializeComponent();
    }

    private void ListView_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is FileListViewModel viewModel
            && viewModel.OpenItemCommand.CanExecute(null))
        {
            viewModel.OpenItemCommand.Execute(null);
        }
    }

    private async void ListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not FileListViewModel viewModel)
        {
            return;
        }

        if (FindShellViewModel() is { } shell)
        {
            await shell.SelectFileAsync(viewModel.SelectedItem);
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
