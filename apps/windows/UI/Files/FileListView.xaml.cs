using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.UI.Shell;

namespace Rynat.WindowsClient.UI.Files;

public partial class FileListView : UserControl
{
    private Point? _dragStartPoint;
    private FileItemViewModel? _dragStartItem;
    private IReadOnlyList<RemoteFileItem>? _dragStartSelection;
    private FileItemViewModel? _remoteDropTarget;

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
        _dragStartSelection = DataContext is FileListViewModel viewModel
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
        if (TryGetRemotePayload(e, out var payload)
            && TryGetDirectoryDropTarget(e, out var target)
            && FindShellViewModel() is { } shell)
        {
            e.Effects = shell.GetRemoteDropEffect(
                payload,
                target.Item.Share,
                target.Item.Path,
                IsCopyRequested(e)
            );
            SetRemoteDropTarget(e.Effects == DragDropEffects.None ? null : target);
            e.Handled = true;
            return;
        }

        SetRemoteDropTarget(null);
        e.Effects = HasFileDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ListView_OnDragLeave(object sender, DragEventArgs e)
    {
        if (!IsMouseWithin((FrameworkElement)sender, e))
        {
            SetRemoteDropTarget(null);
        }
    }

    private async void ListView_OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (TryGetRemotePayload(e, out var payload)
            && TryGetDirectoryDropTarget(e, out var target)
            && FindShellViewModel() is { } remoteShell)
        {
            SetRemoteDropTarget(null);
            await remoteShell.DropRemoteItemsAsync(
                payload,
                target.Item.Share,
                target.Item.Path,
                IsCopyRequested(e)
            );
            return;
        }

        SetRemoteDropTarget(null);
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

    private static bool TryGetDirectoryDropTarget(
        DragEventArgs e,
        out FileItemViewModel target
    )
    {
        target = null!;
        var item = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);
        if (item?.DataContext is not FileItemViewModel { Item.IsDirectory: true } fileItem)
        {
            return false;
        }

        target = fileItem;
        return true;
    }

    private void SetRemoteDropTarget(FileItemViewModel? target)
    {
        if (ReferenceEquals(_remoteDropTarget, target))
        {
            return;
        }

        if (_remoteDropTarget is not null)
        {
            _remoteDropTarget.RemoteDropState = RemoteDropState.None;
        }

        _remoteDropTarget = target;

        if (_remoteDropTarget is not null)
        {
            _remoteDropTarget.RemoteDropState = RemoteDropState.ValidTarget;
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

    private static bool IsCopyRequested(DragEventArgs e)
    {
        return e.KeyStates.HasFlag(DragDropKeyStates.ControlKey);
    }

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
        if (DataContext is not FileListViewModel viewModel)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.X when viewModel.CutCommand.CanExecute(null):
                    viewModel.CutCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.C when viewModel.CopyFileCommand.CanExecute(null):
                    viewModel.CopyFileCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.V when viewModel.PasteCommand.CanExecute(null):
                    viewModel.PasteCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.F:
                    SearchBox.Focus();
                    SearchBox.SelectAll();
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
        if (DataContext is not FileListViewModel viewModel)
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
}
