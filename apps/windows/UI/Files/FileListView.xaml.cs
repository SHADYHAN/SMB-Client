using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Rynat.WindowsClient;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.UI.Shell;

namespace Rynat.WindowsClient.UI.Files;

public partial class FileListView : UserControl
{
    private Point? _dragStartPoint;
    private FileItemViewModel? _dragStartItem;
    private IReadOnlyList<RemoteFileItem>? _dragStartSelection;

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
            _dragStartSelection = null;
            return;
        }

        _dragStartPoint = e.GetPosition(this);
        _dragStartItem = fileItem;
        _dragStartSelection = CurrentViewModel() is { } viewModel
            && item.IsSelected
            && viewModel.SelectedRemoteItems.Any(selected =>
                selected.Share.Equals(fileItem.Item.Share, StringComparison.OrdinalIgnoreCase)
                && NormalizeDirectoryPath(selected.Path).Equals(NormalizeDirectoryPath(fileItem.Item.Path), StringComparison.OrdinalIgnoreCase))
                ? viewModel.SelectedRemoteItems.ToArray()
                : null;
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
        var preservedSelection = _dragStartSelection;
        _dragStartItem = null;
        _dragStartSelection = null;
        if (FindShellViewModel() is { } shell)
        {
            await shell.StartFileDragAsync(this, dragItem, preservedSelection);
        }
    }

    private void ListView_OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(RemoteDragPayload.DataFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = HasFileDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void ListView_OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetDataPresent(RemoteDragPayload.DataFormat))
        {
            return;
        }

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

    private static string NormalizeDirectoryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        var normalized = path.Replace('\\', '/').Trim().TrimEnd('/');
        return normalized.StartsWith('/') ? normalized : "/" + normalized;
    }

    private void ListView_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (CurrentViewModel() is not { } viewModel)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.A when sender is ListView listView:
                    listView.SelectAll();
                    e.Handled = true;
                    return;
                case Key.C when viewModel.CopyLinkCommand.CanExecute(null):
                    viewModel.CopyLinkCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.F:
                    FindMainWindow()?.FocusWorkspaceSearch();
                    e.Handled = true;
                    return;
            }
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)
            && e.Key == Key.C
            && viewModel.CopyLinkCommand.CanExecute(null))
        {
            viewModel.CopyLinkCommand.Execute(null);
            e.Handled = true;
            return;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Alt
            && IsKey(e, Key.Up)
            && viewModel.GoUpCommand.CanExecute(null))
        {
            viewModel.GoUpCommand.Execute(null);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                ClearSearchOrSelection(viewModel, sender as ListView);
                e.Handled = true;
                break;
            case Key.Enter when viewModel.OpenItemCommand.CanExecute(null):
                viewModel.OpenItemCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Back when viewModel.GoUpCommand.CanExecute(null):
                viewModel.GoUpCommand.Execute(null);
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

    private static bool IsKey(KeyEventArgs e, Key key)
    {
        return e.Key == key || e.SystemKey == key;
    }

    private void ClearSearchOrSelection(FileListViewModel viewModel, ListView? listView)
    {
        if (!string.IsNullOrEmpty(viewModel.SearchText))
        {
            viewModel.SearchText = string.Empty;
            listView?.Focus();
            return;
        }

        listView?.SelectedItems.Clear();
        viewModel.SelectedItem = null;
    }

    private void ListView_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CurrentViewModel() is { } viewModel
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

        if (!item.IsSelected)
        {
            if (sender is ListView listView)
            {
                listView.SelectedItems.Clear();
            }

            item.IsSelected = true;
        }

        item.Focus();
    }

    private async void ListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CurrentViewModel() is not { } viewModel)
        {
            return;
        }

        if (sender is ListView listView)
        {
            viewModel.ReplaceSelectedItems(listView.SelectedItems.OfType<FileItemViewModel>());
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

    private MainWindow? FindMainWindow()
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            if (current is MainWindow mainWindow)
            {
                return mainWindow;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private FileListViewModel? CurrentViewModel()
    {
        return DataContext switch
        {
            FileListViewModel viewModel => viewModel,
            ShellViewModel shell => shell.FileList,
            _ => FindShellViewModel()?.FileList
        };
    }
}
