using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Rynat.WindowsClient.UI.Shell;

namespace Rynat.WindowsClient.UI.Files;

public partial class FileListView : UserControl
{
    private Point? _dragStartPoint;
    private FileItemViewModel? _dragStartItem;

    public FileListView()
    {
        InitializeComponent();
    }

    private void ListView_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);
        if (item?.DataContext is not FileItemViewModel fileItem)
        {
            _dragStartPoint = null;
            _dragStartItem = null;
            return;
        }

        _dragStartPoint = e.GetPosition(this);
        _dragStartItem = fileItem;
    }

    private async void ListView_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed
            || _dragStartPoint is not { } startPoint
            || _dragStartItem is not { } dragItem)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);
        if (Math.Abs(currentPoint.X - startPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPoint.Y - startPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStartPoint = null;
        _dragStartItem = null;
        if (FindShellViewModel() is { } shell)
        {
            await shell.StartFileDragAsync(this, dragItem);
        }
    }

    private void ListView_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is FileListViewModel viewModel
            && viewModel.OpenItemCommand.CanExecute(null))
        {
            viewModel.OpenItemCommand.Execute(null);
        }
    }

    private void ListView_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);
        if (item is null)
        {
            return;
        }

        item.IsSelected = true;
        item.Focus();
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
