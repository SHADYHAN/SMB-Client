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

    private void ListView_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasFileDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void ListView_OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!HasFileDrop(e) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        if (FindShellViewModel() is { } shell)
        {
            await shell.UploadDroppedFilesAsync(paths);
        }
    }

    private static bool HasFileDrop(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop);

    private void ListView_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not FileListViewModel viewModel)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.C when viewModel.CopyLinkCommand.CanExecute(null):
                    viewModel.CopyLinkCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.F:
                    SearchBox.Focus();
                    SearchBox.SelectAll();
                    e.Handled = true;
                    return;
            }
        }

        switch (e.Key)
        {
            case Key.Enter when viewModel.OpenItemCommand.CanExecute(null):
                viewModel.OpenItemCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F5 when viewModel.RefreshCommand.CanExecute(null):
                viewModel.RefreshCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Delete when viewModel.DeleteCommand.CanExecute(null):
                viewModel.DeleteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F2 when viewModel.RenameCommand.CanExecute(null):
                viewModel.RenameCommand.Execute(null);
                e.Handled = true;
                break;
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
