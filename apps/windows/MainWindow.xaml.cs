using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using WinRT.Interop;
using System.Collections.ObjectModel;
using System.Text.Json;
using Windows.Media.Editing;
using Windows.Storage.FileProperties;
using Windows.System;
using Windows.Graphics;
using CoreVirtualKeyStates = Windows.UI.Core.CoreVirtualKeyStates;
using Windows.ApplicationModel.DataTransfer;
using Rynat.WindowsClient.UI.Main;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Windows.Data.Pdf;
using Windows.Storage.Pickers;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;
using Rynat.WindowsClient.AppServices.Files;
using Rynat.Client;

namespace Rynat.WindowsClient;

public sealed partial class MainWindow : Window
{
    private static readonly SizeInt32 LoginWindowSize = new(720, 574);
    private static readonly SizeInt32 MinimumLoginWindowSize = new(640, 520);
    private static readonly SizeInt32 WorkspaceWindowSize = new(1088, 680);
    private static readonly SizeInt32 MinimumWorkspaceWindowSize = new(1088, 520);
    private const string InternalDirectoryDragFormat = "Rynat.WindowsClient.InternalDirectoryDrag";
    private const double SidebarPaneDefaultWidth = 220;
    private const double SidebarPaneMinWidth = 180;
    private const double SidebarPaneMaxWidth = 320;
    private const double PreviewPaneDefaultWidth = 340;
    private const double PreviewPaneMinWidth = 280;
    private const double PreviewPaneMaxWidth = 460;
    private const double FilePaneMinWidth = 520;
    private const double WorkspaceSplitterWidth = 10;
    private const double DirectoryNameMinWidth = 180;
    private const double DirectoryTypeMinWidth = 64;
    private const double DirectorySizeMinWidth = 68;
    private const double DirectoryModifiedMinWidth = 146;

    private bool _initialized;
    private string? _lastPreviewImagePath;
    private string? _lastPreviewPdfPath;
    private string? _lastPreviewVideoPath;
    private int _previewRenderVersion;
    private CancellationTokenSource? _sidebarTapOpenCancellation;
    private bool _isClosed;
    private bool _usingWorkspaceWindowSize;
    private bool _workspaceSplitLoaded;
    private bool _isApplyingWorkspaceSplitLayout;
    private WorkspacePaneSplitDrag? _activeSplitDrag;
    private DirectoryColumnDrag? _activeDirectoryColumnDrag;
    private double _sidebarPaneWidth = SidebarPaneDefaultWidth;
    private double _previewPaneWidth = PreviewPaneDefaultWidth;
    private double _directoryNameRatio = 0.58;
    private double _directoryTypeRatio = 0.11;
    private double _directorySizeRatio = 0.12;
    private double _directoryModifiedRatio = 0.19;
    private bool _isApplyingDirectoryColumns;
    private IReadOnlyList<DirectoryItemViewModel>? _contextMenuDirectoryItems;
    private IReadOnlyList<DirectoryItemViewModel> _lastDirectorySelection = [];
    private IReadOnlyList<DirectoryItemViewModel> _lastDirectoryMultiSelection = [];
    private bool _isRestoringDirectorySelection;

    public MainWindow(MainShellViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DirectoryItemsListView.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(DirectoryItemsListView_PointerPressed),
            handledEventsToo: true
        );
        ConfigureInitialWindowSize();
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.PasswordInputClearRequested += ViewModel_PasswordInputClearRequested;
        _ = UpdatePreviewContentAsync();
    }

    private void ViewModel_PasswordInputClearRequested()
    {
        ClearPasswordInput();
    }

    public MainShellViewModel ViewModel { get; }

    private void ConfigureInitialWindowSize()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "RynatApp.ico"));
        ConfigureWindowSize(windowId, appWindow, LoginWindowSize, minimumSize: MinimumLoginWindowSize);
    }

    private static void ConfigureWindowSize(WindowId windowId, AppWindow appWindow, SizeInt32 targetSize, SizeInt32? minimumSize = null)
    {
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = minimumSize?.Width ?? MinimumWorkspaceWindowSize.Width;
            presenter.PreferredMinimumHeight = minimumSize?.Height ?? MinimumWorkspaceWindowSize.Height;
        }

        appWindow.Resize(targetSize);
        if (DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary) is { } displayArea)
        {
            var workArea = displayArea.WorkArea;
            appWindow.Move(new PointInt32(
                workArea.X + Math.Max(0, (workArea.Width - targetSize.Width) / 2),
                workArea.Y + Math.Max(0, (workArea.Height - targetSize.Height) / 2)
            ));
        }
    }

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_isClosed)
        {
            return;
        }

        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await ViewModel.InitializeAsync();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _isClosed = true;
        Interlocked.Increment(ref _previewRenderVersion);
        Activated -= MainWindow_Activated;
        Closed -= MainWindow_Closed;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.PasswordInputClearRequested -= ViewModel_PasswordInputClearRequested;
        CancelPendingSidebarTapOpen();
        ResetPreviewMedia(clearCacheKeys: true);
        ViewModel.Dispose();
    }

    private async void ReloadBootstrapButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadBootstrapAsync();
    }

    private async void SetActiveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SetSelectedProfileAsActiveAsync();
    }

    private async void ConnectProfileButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ConnectSelectedProfileAsync();
    }

    private async void ConnectWithCredentialsButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ConnectWithCredentialsAsync();
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoginAsync();
    }

    private async void ServerSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowServerSettingsDialogAsync();
    }

    private void LogoutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Logout();
    }

    private void CancelActiveTaskButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelActiveTask();
    }

    private void CopyStatusPathButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CopySelectedPath();
    }

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
        {
            ViewModel.SetManualPassword(passwordBox.Password);
        }
    }

    private void UsernameFieldFrame_Tapped(object sender, TappedRoutedEventArgs e)
    {
        UsernameInput.Focus(FocusState.Programmatic);
    }

    private void PasswordFieldFrame_Tapped(object sender, TappedRoutedEventArgs e)
    {
        PasswordInput.Focus(FocusState.Programmatic);
    }

    private void LoginTextInput_GotFocus(object sender, RoutedEventArgs e)
    {
        var frame = ReferenceEquals(sender, UsernameInput)
            ? UsernameFieldFrame
            : PasswordFieldFrame;
        var icon = ReferenceEquals(sender, UsernameInput)
            ? UsernameFieldIcon
            : PasswordFieldIcon;
        frame.Background = (Brush)Application.Current.Resources["RynatLoginFieldFocusedBrush"];
        frame.BorderBrush = (Brush)Application.Current.Resources["RynatLoginFieldFocusedBorderBrush"];
        icon.Foreground = (Brush)Application.Current.Resources["RynatFaintBrush"];
    }

    private void LoginTextInput_LostFocus(object sender, RoutedEventArgs e)
    {
        var frame = ReferenceEquals(sender, UsernameInput)
            ? UsernameFieldFrame
            : PasswordFieldFrame;
        var icon = ReferenceEquals(sender, UsernameInput)
            ? UsernameFieldIcon
            : PasswordFieldIcon;
        frame.Background = (Brush)Application.Current.Resources["RynatLoginFieldBrush"];
        frame.BorderBrush = (Brush)Application.Current.Resources["RynatLoginFieldBorderBrush"];
        icon.Foreground = (Brush)Application.Current.Resources["RynatFaintBrush"];
    }

    private async void LoginTextInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || !ViewModel.CanLogin)
        {
            return;
        }

        e.Handled = true;
        await ViewModel.LoginAsync();
    }

    private void ShareSidebarTab_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowShareSidebar();
    }

    private async void FavoriteSidebarTab_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ShowFavoriteSidebarAsync();
    }

    private async void SidebarListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        CancelPendingSidebarTapOpen();
        if (e.OriginalSource is not DependencyObject sourceElement)
        {
            return;
        }

        if (FindAncestor<ListViewItem>(sourceElement) is not ListViewItem container
            || container.Content is not SidebarItemViewModel item)
        {
            return;
        }

        if (IsSidebarExpansionHit(item, e.GetPosition(container).X))
        {
            e.Handled = true;
            return;
        }

        await ViewModel.OpenAndToggleSidebarItemAsync(item);
        e.Handled = true;
    }

    private async void SidebarListView_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject sourceElement)
        {
            return;
        }

        if (FindAncestor<ListViewItem>(sourceElement) is not ListViewItem container
            || container.Content is not SidebarItemViewModel item)
        {
            return;
        }

        ViewModel.SelectedSidebarItem = item;
        if (IsSidebarExpansionHit(item, e.GetPosition(container).X))
        {
            CancelPendingSidebarTapOpen();
            await ViewModel.ToggleSidebarItemExpansionAsync(item);
            e.Handled = true;
            return;
        }

        QueueSidebarTapOpen(item);
        e.Handled = true;
    }

    private void QueueSidebarTapOpen(SidebarItemViewModel item)
    {
        CancelPendingSidebarTapOpen();
        var cancellation = new CancellationTokenSource();
        _sidebarTapOpenCancellation = cancellation;
        _ = OpenSidebarItemAfterTapDelayAsync(item, cancellation);
    }

    private async Task OpenSidebarItemAfterTapDelayAsync(SidebarItemViewModel item, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(220, cancellation.Token);
            if (cancellation.IsCancellationRequested || _isClosed)
            {
                return;
            }

            await ViewModel.OpenSidebarItemAsync(item);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_sidebarTapOpenCancellation, cancellation))
            {
                _sidebarTapOpenCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void CancelPendingSidebarTapOpen()
    {
        var cancellation = _sidebarTapOpenCancellation;
        if (cancellation is null)
        {
            return;
        }

        _sidebarTapOpenCancellation = null;
        cancellation.Cancel();
    }

    private void SidebarListView_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject sourceElement)
        {
            return;
        }

        if (FindAncestor<ListViewItem>(sourceElement) is not ListViewItem container
            || container.Content is not SidebarItemViewModel item)
        {
            return;
        }

        ViewModel.SelectedSidebarItem = item;
        if (!item.IsFavorite)
        {
            return;
        }

        var menu = new MenuFlyout();
        AddMenuItem(menu, "打开收藏", async () => await ViewModel.OpenSelectedSidebarItemAsync(), true);
        menu.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(menu, "取消收藏", async () => await ViewModel.RemoveSelectedFavoriteAsync(), ViewModel.CanRemoveSelectedFavorite);
        menu.ShowAt(SidebarListView, e.GetPosition(SidebarListView));
    }

    private async void NavigateUpButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.NavigateUpAsync();
    }

    private async void OpenShareRootButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.OpenShareRootAsync();
    }

    private async void CreateDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        await PromptAndCreateDirectoryAsync();
    }

    private async void UploadFilesButton_Click(object sender, RoutedEventArgs e)
    {
        await PickAndUploadFilesAsync();
    }

    private async Task PromptAndCreateDirectoryAsync()
    {
        var name = await PromptForTextAsync("新建文件夹", "请输入文件夹名称", "新建文件夹");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await ViewModel.CreateDirectoryAsync(name);
    }

    private async Task PickAndUploadFilesAsync()
    {
        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0)
        {
            return;
        }

        var localPaths = files.Select(file => file.Path).ToArray();
        var conflictDecisions = await ResolveUploadConflictsAsync(localPaths);
        if (conflictDecisions is null)
        {
            return;
        }

        await ViewModel.UploadFilesAsync(localPaths, conflictDecisions);
    }

    private async void OpenDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.OpenSelectedItemAsync();
    }

    private async void DownloadSelectedItemButton_Click(object sender, RoutedEventArgs e)
    {
        await PickAndDownloadSelectedItemAsync();
    }

    private async Task PickAndDownloadSelectedItemAsync()
    {
        var selectedItems = GetSelectedDirectoryItems();
        await PickAndDownloadItemsAsync(selectedItems);
    }

    private async Task PickAndDownloadItemsAsync(IReadOnlyList<DirectoryItemViewModel> selectedItems)
    {
        if (selectedItems.Count == 0)
        {
            return;
        }

        if (selectedItems.Count > 1)
        {
            var folderPicker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.Downloads
            };
            folderPicker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(folderPicker, WindowNative.GetWindowHandle(this));

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            await ViewModel.DownloadSelectedItemsAsync(selectedItems, folder.Path);
            return;
        }

        var selected = selectedItems[0];
        ViewModel.SelectedDirectoryItem = selected;
        if (selected.IsDirectory)
        {
            var folderPicker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.Downloads
            };
            folderPicker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(folderPicker, WindowNative.GetWindowHandle(this));

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            await ViewModel.DownloadSelectedItemAsync(folder.Path);
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
            SuggestedFileName = selected.Name
        };

        var extension = Path.GetExtension(selected.Name);
        if (string.IsNullOrWhiteSpace(extension))
        {
            picker.FileTypeChoices.Add("所有文件", ["*"]);
        }
        else
        {
            picker.FileTypeChoices.Add($"{extension.TrimStart('.').ToUpperInvariant()} 文件", [extension]);
        }

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        await ViewModel.DownloadSelectedItemAsync(file.Path);
    }

    private async void RenameItemButton_Click(object sender, RoutedEventArgs e)
    {
        await PromptAndRenameSelectedItemAsync();
    }

    private async Task PromptAndRenameSelectedItemAsync()
    {
        var currentName = ViewModel.SelectedDirectoryItem?.Name;
        if (string.IsNullOrWhiteSpace(currentName))
        {
            return;
        }

        var newName = await PromptForTextAsync("重命名", "请输入新的名称", currentName);
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, currentName, StringComparison.Ordinal))
        {
            return;
        }

        await ViewModel.RenameSelectedItemAsync(newName);
    }

    private async void DeleteItemButton_Click(object sender, RoutedEventArgs e)
    {
        await ConfirmAndDeleteSelectedItemAsync();
    }

    private async Task ConfirmAndDeleteSelectedItemAsync()
    {
        var selectedItems = GetSelectedDirectoryItems();
        await ConfirmAndDeleteItemsAsync(selectedItems);
    }

    private async Task ConfirmAndDeleteItemsAsync(IReadOnlyList<DirectoryItemViewModel> selectedItems)
    {
        if (selectedItems.Count == 0)
        {
            return;
        }

        var currentName = selectedItems[0].Name;
        var message = selectedItems.Count == 1
            ? $"确认删除“{currentName}”？如果是文件夹，会递归删除其中内容。"
            : $"确认删除 {selectedItems.Count} 个项目？\n{BuildSelectedItemsSummary(selectedItems)}";
        var confirmed = await ConfirmAsync(
            "删除项目",
            message
        );
        if (!confirmed)
        {
            return;
        }

        await ViewModel.DeleteSelectedItemsAsync(selectedItems);
    }

    private void CopyItemButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CopySelectedItems(GetSelectedDirectoryItems());
    }

    private void CutItemButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CutSelectedItems(GetSelectedDirectoryItems());
    }

    private async void PasteItemButton_Click(object sender, RoutedEventArgs e)
    {
        await PasteClipboardWithConflictResolutionAsync();
    }

    private async Task PasteClipboardWithConflictResolutionAsync()
    {
        var conflictDecisions = await ResolvePasteConflictsAsync();
        if (conflictDecisions is null)
        {
            return;
        }

        await ViewModel.PasteClipboardAsync(conflictDecisions);
    }

    private async void RefreshDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshCurrentDirectoryAsync();
    }

    private void TogglePreviewButton_Click(object sender, RoutedEventArgs e)
    {
        CaptureWorkspaceSplitWidths();
        ViewModel.TogglePreviewPane();
    }

    private void WorkspaceSplitGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyWorkspaceSplitLayout();
    }

    private void DirectoryContentGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyDirectoryColumnLayout();
    }

    private void SidebarPaneSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        StartWorkspaceSplitDrag(WorkspaceSplitPane.Sidebar, SidebarPaneSplitter, e);
    }

    private void PreviewPaneSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        StartWorkspaceSplitDrag(WorkspaceSplitPane.Preview, PreviewPaneSplitter, e);
    }

    private void WorkspacePaneSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_activeSplitDrag is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(WorkspaceSplitGrid);
        var delta = point.Position.X - _activeSplitDrag.StartX;
        if (_activeSplitDrag.Pane == WorkspaceSplitPane.Sidebar)
        {
            _sidebarPaneWidth = ClampPaneWidth(
                _activeSplitDrag.StartWidth + delta,
                SidebarPaneMinWidth,
                EffectiveSidebarPaneMaxWidth());
        }
        else
        {
            _previewPaneWidth = ClampPaneWidth(
                _activeSplitDrag.StartWidth - delta,
                PreviewPaneMinWidth,
                EffectivePreviewPaneMaxWidth());
        }

        ApplyWorkspaceSplitLayout();
        e.Handled = true;
    }

    private void WorkspacePaneSplitter_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        SetWorkspaceSplitterLine(sender, true);
    }

    private void WorkspacePaneSplitter_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_activeSplitDrag is not null)
        {
            return;
        }

        SetWorkspaceSplitterLine(sender, false);
    }

    private void SidebarPaneSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndWorkspaceSplitDrag(SidebarPaneSplitter, e);
    }

    private void PreviewPaneSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndWorkspaceSplitDrag(PreviewPaneSplitter, e);
    }

    private void DirectoryColumnSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement splitter || ColumnForDirectorySplitter(splitter) is not { } column)
        {
            return;
        }

        var pointer = e.GetCurrentPoint(DirectoryItemsListView);
        _activeDirectoryColumnDrag = new DirectoryColumnDrag(
            column,
            pointer.Position.X,
            DirectoryColumnWidth(column)
        );
        splitter.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void DirectoryColumnSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_activeDirectoryColumnDrag is null)
        {
            return;
        }

        var pointer = e.GetCurrentPoint(DirectoryItemsListView);
        var delta = pointer.Position.X - _activeDirectoryColumnDrag.StartX;
        SetDirectoryColumnWidth(
            _activeDirectoryColumnDrag.Column,
            _activeDirectoryColumnDrag.StartWidth + delta
        );
        CaptureDirectoryColumnRatios();
        e.Handled = true;
    }

    private void DirectoryColumnSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_activeDirectoryColumnDrag is null)
        {
            return;
        }

        _activeDirectoryColumnDrag = null;
        if (sender is UIElement splitter)
        {
            splitter.ReleasePointerCapture(e.Pointer);
        }

        CaptureDirectoryColumnRatios();
        ApplyDirectoryColumnLayout();
        e.Handled = true;
    }

    private void DirectoryDropTarget_DragOver(object sender, DragEventArgs e)
    {
        HideDragDropBadge(e);
        if (e.DataView.Contains(InternalDirectoryDragFormat))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.Handled = true;
            return;
        }

        e.AcceptedOperation = ViewModel.CanUploadFiles && e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
        e.Handled = true;
    }

    private static void HideDragDropBadge(DragEventArgs e)
    {
        e.DragUIOverride.IsCaptionVisible = false;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsContentVisible = false;
    }

    private async void DirectoryDropTarget_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(InternalDirectoryDragFormat))
        {
            e.Handled = true;
            return;
        }

        if (!ViewModel.CanUploadFiles || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        var localPaths = items
            .Select(item => item.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();

        if (localPaths.Length == 0)
        {
            await ShowMessageAsync("无法上传", "请拖入一个或多个本地文件或文件夹。");
            return;
        }

        var conflictDecisions = await ResolveUploadConflictsAsync(localPaths);
        if (conflictDecisions is null)
        {
            return;
        }

        await ViewModel.UploadFilesAsync(localPaths, conflictDecisions);
        e.Handled = true;
    }

    private async void DirectoryItemsListView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.FirstOrDefault() is not DirectoryItemViewModel item)
        {
            e.Cancel = true;
            return;
        }

        ViewModel.SelectedDirectoryItem = item;
        try
        {
            var selectedItems = GetSelectedDirectoryItems();
            if (!selectedItems.Contains(item))
            {
                selectedItems = [item];
            }

            var result = await ViewModel.PrepareItemsForDragDownloadAsync(selectedItems);
            if (!result.Succeeded || result.Items.Count == 0)
            {
                e.Cancel = true;
                return;
            }

            var storageItems = new List<IStorageItem>(result.Items.Count);
            foreach (var preparedItem in result.Items)
            {
                var storageItem = preparedItem.Source.IsDirectory
                    ? await StorageFolder.GetFolderFromPathAsync(preparedItem.LocalPath)
                    : (IStorageItem)await StorageFile.GetFileFromPathAsync(preparedItem.LocalPath);
                storageItems.Add(storageItem);
            }

            e.Data.SetStorageItems(storageItems);
            e.Data.SetData(InternalDirectoryDragFormat, "1");
            e.Data.RequestedOperation = DataPackageOperation.Copy;
        }
        catch
        {
            e.Cancel = true;
        }
    }

    private async void DirectoryItemsListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject sourceElement)
        {
            return;
        }

        if (FindAncestor<ListViewItem>(sourceElement) is not ListViewItem container
            || container.Content is not DirectoryItemViewModel item)
        {
            return;
        }

        ViewModel.SelectedDirectoryItem = item;
        await ViewModel.OpenSelectedItemAsync();
    }

    private void DirectoryItemsListView_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject sourceElement)
        {
            return;
        }

        if (FindAncestor<ListViewItem>(sourceElement) is not null)
        {
            return;
        }

        ClearDirectorySelection();
        e.Handled = true;
    }

    private void DirectoryItemsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRestoringDirectorySelection)
        {
            return;
        }

        var selectedItems = DirectoryItemsListView.SelectedItems
            .OfType<DirectoryItemViewModel>()
            .ToArray();
        var previousSelection = _lastDirectorySelection;
        _lastDirectorySelection = selectedItems;

        if (selectedItems.Length > 1)
        {
            _lastDirectoryMultiSelection = selectedItems;
            return;
        }

        if (selectedItems.Length == 0)
        {
            _lastDirectoryMultiSelection = [];
            _contextMenuDirectoryItems = null;
            return;
        }

        if (previousSelection.Count > 1
            && previousSelection.Contains(selectedItems[0])
            && IsRightButtonPressed())
        {
            // WinUI can temporarily collapse Extended selection to the right-clicked row.
            _lastDirectoryMultiSelection = previousSelection;
            return;
        }

        _lastDirectoryMultiSelection = [];
    }

    private void DirectoryItemsListView_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(DirectoryItemsListView);
        if (!pointer.Properties.IsRightButtonPressed)
        {
            _contextMenuDirectoryItems = null;
            return;
        }

        if (e.OriginalSource is not DependencyObject sourceElement
            || FindAncestor<ListViewItem>(sourceElement) is not ListViewItem container
            || container.Content is not DirectoryItemViewModel item)
        {
            _contextMenuDirectoryItems = null;
            return;
        }

        var selectedItems = ResolveDirectoryContextSelection(item);

        _contextMenuDirectoryItems = selectedItems.Contains(item)
            ? selectedItems
            : [item];
    }

    private void ClearDirectorySelection()
    {
        DirectoryItemsListView.SelectedItems.Clear();
        _lastDirectorySelection = [];
        _lastDirectoryMultiSelection = [];
        ViewModel.SelectedDirectoryItem = null;
    }

    private IReadOnlyList<DirectoryItemViewModel> GetSelectedDirectoryItems()
    {
        var selectedItems = DirectoryItemsListView.SelectedItems
            .OfType<DirectoryItemViewModel>()
            .ToArray();
        if (selectedItems.Length > 0)
        {
            return selectedItems;
        }

        return ViewModel.SelectedDirectoryItem is null
            ? []
            : [ViewModel.SelectedDirectoryItem];
    }

    private IReadOnlyList<DirectoryItemViewModel> ResolveDirectoryContextSelection(DirectoryItemViewModel item)
    {
        var selectedItems = GetSelectedDirectoryItems();
        if (selectedItems.Count <= 1
            && _lastDirectoryMultiSelection.Count > 1
            && _lastDirectoryMultiSelection.Contains(item))
        {
            return _lastDirectoryMultiSelection;
        }

        return selectedItems.Contains(item)
            ? selectedItems
            : [item];
    }

    private void EnsureDirectoryItemSelectionForContextMenu(DirectoryItemViewModel item)
    {
        var contextItems = _contextMenuDirectoryItems;
        if (contextItems is not null && contextItems.Contains(item) && contextItems.Count > 1)
        {
            RestoreDirectorySelection(contextItems);
            return;
        }
        else if (!DirectoryItemsListView.SelectedItems.Contains(item))
        {
            RestoreDirectorySelection([item]);
            _contextMenuDirectoryItems = [item];
        }

        ViewModel.SelectedDirectoryItem = item;
    }

    private void RestoreDirectorySelection(IReadOnlyList<DirectoryItemViewModel> items)
    {
        _isRestoringDirectorySelection = true;
        try
        {
            DirectoryItemsListView.SelectedItems.Clear();
            foreach (var item in items)
            {
                DirectoryItemsListView.SelectedItems.Add(item);
            }
        }
        finally
        {
            _isRestoringDirectorySelection = false;
        }

        _lastDirectorySelection = items.ToArray();
        _lastDirectoryMultiSelection = _lastDirectorySelection.Count > 1
            ? _lastDirectorySelection
            : [];
    }

    private static string BuildSelectedItemsSummary(IReadOnlyList<DirectoryItemViewModel> items)
    {
        var names = items
            .Take(8)
            .Select(item => item.Name);
        var summary = string.Join("、", names);
        return items.Count > 8
            ? $"{summary} 等"
            : summary;
    }

    private async void DirectoryItemsListView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrlPressed = IsCtrlPressed();
        if (ctrlPressed && e.Key == VirtualKey.C)
        {
            ViewModel.CopySelectedItems(GetSelectedDirectoryItems());
            e.Handled = true;
            return;
        }

        if (ctrlPressed && e.Key == VirtualKey.X)
        {
            ViewModel.CutSelectedItems(GetSelectedDirectoryItems());
            e.Handled = true;
            return;
        }

        if (ctrlPressed && e.Key == VirtualKey.V)
        {
            await PasteClipboardWithConflictResolutionAsync();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Enter:
                await ViewModel.OpenSelectedItemAsync();
                e.Handled = true;
                break;
            case VirtualKey.Delete:
                await ConfirmAndDeleteSelectedItemAsync();
                e.Handled = true;
                break;
            case VirtualKey.F2:
                await PromptAndRenameSelectedItemAsync();
                e.Handled = true;
                break;
        }
    }

    private void DirectoryItemsListView_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject sourceElement)
        {
            ShowDirectoryBackgroundMenu(e);
            return;
        }

        if (FindAncestor<ListViewItem>(sourceElement) is ListViewItem container
            && container.Content is DirectoryItemViewModel item)
        {
            EnsureDirectoryItemSelectionForContextMenu(item);
            ShowDirectoryItemMenu(e, item);
            return;
        }

        ShowDirectoryBackgroundMenu(e);
    }

    private void ShowDirectoryItemMenu(RightTappedRoutedEventArgs e, DirectoryItemViewModel item)
    {
        var menu = new MenuFlyout();
        var selectedItems = _contextMenuDirectoryItems ?? GetSelectedDirectoryItems();
        if (selectedItems.Count == 0)
        {
            selectedItems = [item];
        }
        var isSingleSelection = selectedItems.Count == 1;

        if (isSingleSelection)
        {
            AddMenuItem(
                menu,
                item.IsDirectory ? "打开文件夹" : "打开文件",
                async () => await ViewModel.OpenSelectedItemAsync(),
                ViewModel.CanOpenSelectedItem
            );
        }
        if (ViewModel.CurrentPath != "/")
        {
            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
            }
            AddMenuItem(menu, "剪切", () => ViewModel.CutSelectedItems(selectedItems), selectedItems.Count > 0 && ViewModel.CanCutSelectedItem);
            AddMenuItem(menu, "复制", () => ViewModel.CopySelectedItems(selectedItems), selectedItems.Count > 0 && ViewModel.CanCopySelectedItem);
            if (isSingleSelection)
            {
                AddMenuItem(menu, "重命名", async () => await PromptAndRenameSelectedItemAsync(), ViewModel.CanRenameSelectedItem);
            }
            AddMenuItem(menu, "删除", async () => await ConfirmAndDeleteItemsAsync(selectedItems), selectedItems.Count > 0 && ViewModel.CanDeleteSelectedItem);
        }
        if (isSingleSelection)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem(menu, "生成分享链接", async () => await ViewModel.GenerateLinkForSelectionAsync(), ViewModel.CanGenerateLink);
            AddMenuItem(menu, "添加到收藏", async () => await ViewModel.AddSelectedItemToFavoritesAsync(), ViewModel.CanAddSelectedItemToFavorites);
            AddMenuItem(menu, "复制路径", () => ViewModel.CopySelectedPath(), ViewModel.CanCopySelectedPath);
            if (!item.IsDirectory)
            {
                AddMenuItem(menu, "播放/预览", async () => await ViewModel.PlaySelectedVideoAsync(), ViewModel.CanPlaySelectedVideo);
            }
        }
        AddMenuItem(menu, "下载到本地", async () => await PickAndDownloadItemsAsync(selectedItems), selectedItems.Count > 0 && ViewModel.CanDownloadSelectedItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(menu, "刷新", async () => await ViewModel.RefreshCurrentDirectoryAsync(), true);

        menu.ShowAt(DirectoryItemsListView, e.GetPosition(DirectoryItemsListView));
    }

    private void ShowDirectoryBackgroundMenu(RightTappedRoutedEventArgs e)
    {
        _contextMenuDirectoryItems = null;
        var menu = new MenuFlyout();

        AddMenuItem(menu, "新建文件夹", async () => await PromptAndCreateDirectoryAsync(), ViewModel.CanCreateDirectory);
        AddMenuItem(menu, "上传文件", async () => await PickAndUploadFilesAsync(), ViewModel.CanUploadFiles);
        if (ViewModel.HasClipboardItems)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem(menu, ViewModel.ClipboardPasteMenuText, async () => await PasteClipboardWithConflictResolutionAsync(), ViewModel.CanPasteClipboard);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(menu, "刷新", async () => await ViewModel.RefreshCurrentDirectoryAsync(), true);

        menu.ShowAt(DirectoryItemsListView, e.GetPosition(DirectoryItemsListView));
    }

    private static void AddMenuItem(MenuFlyout menu, string text, Action action, bool isEnabled)
    {
        AddMenuItem(
            menu,
            text,
            () =>
            {
                action();
                return Task.CompletedTask;
            },
            isEnabled
        );
    }

    private static void AddMenuItem(MenuFlyout menu, string text, Func<Task> action, bool isEnabled)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            IsEnabled = isEnabled
        };
        item.Click += async (_, _) => await action();
        menu.Items.Add(item);
    }

    private static bool IsCtrlPressed()
    {
        var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        return ctrlState.HasFlag(CoreVirtualKeyStates.Down);
    }

    private static bool IsRightButtonPressed()
    {
        var rightButtonState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.RightButton);
        return rightButtonState.HasFlag(CoreVirtualKeyStates.Down);
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
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

    private static bool IsSidebarExpansionHit(SidebarItemViewModel item, double x)
    {
        if (!item.CanExpand)
        {
            return false;
        }

        var indent = Math.Min(item.Depth, 6) * 14;
        return x >= 8 + indent && x <= 30 + indent;
    }

    private async void GenerateLinkButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.GenerateLinkForSelectionAsync();
    }

    private async void PlaySelectedVideoButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.PlaySelectedVideoAsync();
    }

    private async Task ShowServerSettingsDialogAsync()
    {
        var snapshot = ViewModel.BootstrapSnapshot;
        var profiles = snapshot?.ServerProfiles ?? [];
        var activeProfileId = snapshot?.ActiveServer?.Id;
        var editorItems = new ObservableCollection<ServerProfileEditorItem>(
            profiles.Select(profile => ServerProfileEditorItem.FromProfile(profile, activeProfileId)));

        var profileList = new ListView
        {
            Width = 198,
            Height = 218,
            ItemsSource = editorItems,
            SelectionMode = ListViewSelectionMode.Single,
            ItemContainerStyle = (Style)Application.Current.Resources["RynatServerProfileListViewItemStyle"],
            ItemTemplate = CreateServerProfileItemTemplate()
        };

        var nameInput = new TextBox
        {
            Header = "名称",
            PlaceholderText = "共享网盘"
        };
        var hostInput = new TextBox
        {
            Header = "地址",
            PlaceholderText = "192.168.102.136"
        };
        var setActiveInput = new CheckBox { Content = "设为当前服务器" };

        var selectedEditorItem = editorItems
            .FirstOrDefault(item => string.Equals(item.Id, activeProfileId, StringComparison.OrdinalIgnoreCase))
            ?? editorItems.FirstOrDefault();
        if (selectedEditorItem is not null)
        {
            profileList.SelectedItem = selectedEditorItem;
            ApplyServerEditorItem(selectedEditorItem, nameInput, hostInput, setActiveInput, activeProfileId);
        }

        var addButton = CreateServerManagerIconButton("\uE710", "新增服务器");
        var removeButton = CreateServerManagerIconButton("\uE738", "删除服务器");
        ServerProfileEditorItem? pendingDeleteItem = null;
        ContentDialog? serverDialog = null;
        removeButton.IsEnabled = selectedEditorItem is not null
            && !selectedEditorItem.IsNew
            && editorItems.Count > 1;

        void RefreshRemoveButton()
        {
            removeButton.IsEnabled = profileList.SelectedItem is ServerProfileEditorItem selected
                && !selected.IsNew
                && editorItems.Count > 1;
        }

        addButton.Click += (_, _) =>
        {
            var newItem = ServerProfileEditorItem.New();
            editorItems.Add(newItem);
            profileList.SelectedItem = newItem;
            profileList.ScrollIntoView(newItem);
            ApplyServerEditorItem(newItem, nameInput, hostInput, setActiveInput, activeProfileId);
            RefreshRemoveButton();
        };

        removeButton.Click += async (_, _) =>
        {
            if (profileList.SelectedItem is not ServerProfileEditorItem item)
            {
                return;
            }

            if (item.IsNew)
            {
                editorItems.Remove(item);
                profileList.SelectedItem = editorItems.FirstOrDefault(editor => string.Equals(editor.Id, activeProfileId, StringComparison.OrdinalIgnoreCase))
                    ?? editorItems.FirstOrDefault();
                RefreshRemoveButton();
                return;
            }

            if (editorItems.Count <= 1)
            {
                await ShowMessageAsync("无法删除", "至少需要保留一个服务器。");
                return;
            }

            pendingDeleteItem = item;
            // Deletion needs a confirmation dialog, so close this ContentDialog first.
            serverDialog?.Hide();
        };

        profileList.SelectionChanged += (_, _) =>
        {
            if (profileList.SelectedItem is ServerProfileEditorItem item)
            {
                ApplyServerEditorItem(item, nameInput, hostInput, setActiveInput, activeProfileId);
            }
            RefreshRemoveButton();
        };

        var listContainer = new Border
        {
            Width = 198,
            Height = 218,
            Background = (Brush)Application.Current.Resources["RynatHoverBrush"],
            BorderBrush = (Brush)Application.Current.Resources["RynatLineBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = profileList
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                addButton,
                removeButton
            }
        };

        var leftPane = new StackPanel
        {
            Spacing = 8,
            Width = 198,
            Children =
            {
                listContainer,
                buttonRow
            }
        };

        var rightPane = new StackPanel
        {
            Width = 344,
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "服务器信息",
                    Style = (Style)Application.Current.Resources["RynatSectionTitleStyle"]
                },
                nameInput,
                hostInput,
                setActiveInput
            }
        };

        var root = new Grid
        {
            Width = 560,
            Height = 258,
            ColumnSpacing = 18
        };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(leftPane, 0);
        Grid.SetColumn(rightPane, 1);
        root.Children.Add(leftPane);
        root.Children.Add(rightPane);

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "服务器管理",
            Content = root,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        serverDialog = dialog;

        var result = await dialog.ShowAsync();
        if (pendingDeleteItem is not null)
        {
            var confirmed = await ConfirmAsync(
                "删除服务器",
                $"确认删除“{pendingDeleteItem.DisplayName}”？已保存凭据也会一并移除。"
            );
            if (confirmed)
            {
                await ViewModel.DeleteServerProfileAsync(pendingDeleteItem.Id);
            }
            return;
        }

        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(nameInput.Text)
            ? "共享网盘"
            : nameInput.Text.Trim();
        var host = hostInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            await ShowMessageAsync("地址无效", "请输入服务器地址。");
            return;
        }

        var selectedId = profileList.SelectedItem is ServerProfileEditorItem selected
            ? selected.Id
            : null;
        await ViewModel.SaveServerProfileAsync(selectedId, name, host, setActiveInput.IsChecked == true);
    }

    private static Button CreateServerManagerIconButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Width = 32,
            Height = 28,
            MinWidth = 32,
            MinHeight = 28,
            Padding = new Thickness(0),
            Style = (Style)Application.Current.Resources["RynatSecondaryButtonStyle"],
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 12
            }
        };
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    private static DataTemplate CreateServerProfileItemTemplate()
    {
        const string xaml = """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Grid Padding="12,6,12,6" ColumnSpacing="8">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>
                    <FontIcon
                        Grid.Column="0"
                        VerticalAlignment="Center"
                        FontSize="14"
                        Foreground="{ThemeResource RynatAccentBrush}"
                        Glyph="&#xE73E;"
                        Visibility="{Binding ActiveIndicatorVisibility}" />
                    <StackPanel
                        Grid.Column="1"
                        Spacing="2"
                        VerticalAlignment="Center">
                        <TextBlock
                            FontSize="13"
                            FontWeight="{Binding DisplayNameFontWeight}"
                            MaxLines="1"
                            Text="{Binding DisplayName}"
                            TextTrimming="CharacterEllipsis" />
                        <TextBlock
                            FontSize="11"
                            Foreground="{ThemeResource RynatMutedBrush}"
                            MaxLines="1"
                            Text="{Binding HostPreview}"
                            TextTrimming="CharacterEllipsis" />
                    </StackPanel>
                </Grid>
            </DataTemplate>
            """;

        return (DataTemplate)XamlReader.Load(xaml);
    }

    private static void ApplyServerEditorItem(
        ServerProfileEditorItem item,
        TextBox nameInput,
        TextBox hostInput,
        CheckBox setActiveInput,
        string? activeProfileId
    )
    {
        nameInput.Text = item.DisplayName;
        hostInput.Text = item.Host;
        setActiveInput.IsChecked = string.Equals(item.Id, activeProfileId, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ServerProfileEditorItem(
        string Id,
        string DisplayName,
        string Host,
        bool IsActive
    )
    {
        public bool IsNew => string.IsNullOrWhiteSpace(Id);

        public Visibility ActiveIndicatorVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;

        public string HostPreview => string.IsNullOrWhiteSpace(Host) ? "待填写地址" : Host;

        public Windows.UI.Text.FontWeight DisplayNameFontWeight => IsActive
            ? new Windows.UI.Text.FontWeight { Weight = 600 }
            : new Windows.UI.Text.FontWeight { Weight = 400 };

        public static ServerProfileEditorItem FromProfile(StoredServerProfile profile, string? activeProfileId) =>
            new(
                profile.Id,
                string.IsNullOrWhiteSpace(profile.DisplayName) ? "共享网盘" : profile.DisplayName,
                profile.Endpoint.Host,
                string.Equals(profile.Id, activeProfileId, StringComparison.OrdinalIgnoreCase)
            );

        public static ServerProfileEditorItem New() =>
            new(string.Empty, "新服务器", string.Empty, true);

        public override string ToString() => $"{DisplayName}  {Host}";
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isClosed)
        {
            return;
        }

        if (e.PropertyName is nameof(MainShellViewModel.PreviewPane))
        {
            _ = UpdatePreviewContentAsync();
            return;
        }

        if (e.PropertyName is nameof(MainShellViewModel.IsWorkspaceVisible))
        {
            ApplyWindowSizeForCurrentView();
            ClearPasswordInput();
        }

        if (e.PropertyName is nameof(MainShellViewModel.IsPreviewPaneVisible))
        {
            ApplyWorkspaceSplitLayout();
        }
    }

    private void ApplyWindowSizeForCurrentView()
    {
        if (_usingWorkspaceWindowSize == ViewModel.IsWorkspaceVisible)
        {
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        if (ViewModel.IsWorkspaceVisible)
        {
            ConfigureWindowSize(windowId, appWindow, WorkspaceWindowSize, minimumSize: MinimumWorkspaceWindowSize);
            EnsureWorkspaceSplitStateLoaded();
            ApplyWorkspaceSplitLayout();
        }
        else
        {
            CaptureWorkspaceSplitWidths();
            ConfigureWindowSize(windowId, appWindow, LoginWindowSize, minimumSize: MinimumLoginWindowSize);
        }

        _usingWorkspaceWindowSize = ViewModel.IsWorkspaceVisible;
    }

    private void EnsureWorkspaceSplitStateLoaded()
    {
        if (_workspaceSplitLoaded)
        {
            return;
        }

        _workspaceSplitLoaded = true;
        var state = LoadWorkspaceUiState();
        _sidebarPaneWidth = ClampPaneWidth(
            state?.SidebarWidth ?? SidebarPaneDefaultWidth,
            SidebarPaneMinWidth,
            SidebarPaneMaxWidth);
        _previewPaneWidth = ClampPaneWidth(
            state?.PreviewWidth ?? PreviewPaneDefaultWidth,
            PreviewPaneMinWidth,
            PreviewPaneMaxWidth);
    }

    private void ApplyWorkspaceSplitLayout()
    {
        if (_isApplyingWorkspaceSplitLayout || !ViewModel.IsWorkspaceVisible)
        {
            return;
        }

        _isApplyingWorkspaceSplitLayout = true;
        try
        {
            var totalWidth = WorkspaceSplitGrid.ActualWidth;
            if (!double.IsFinite(totalWidth) || totalWidth <= 0)
            {
                return;
            }

            var hasPreview = ViewModel.IsPreviewPaneVisible;
            var dividerWidth = WorkspaceSplitterWidth * (hasPreview ? 2 : 1);
            var desiredPreviewWidth = hasPreview ? _previewPaneWidth : 0;
            var availableForSidebar = totalWidth - FilePaneMinWidth - desiredPreviewWidth - dividerWidth;
            var sidebarMax = Math.Max(SidebarPaneMinWidth, Math.Min(SidebarPaneMaxWidth, availableForSidebar));
            var sidebarWidth = ClampPaneWidth(_sidebarPaneWidth, SidebarPaneMinWidth, sidebarMax);

            var previewWidth = 0d;
            if (hasPreview)
            {
                var availableForPreview = totalWidth - sidebarWidth - FilePaneMinWidth - dividerWidth;
                var previewMax = Math.Max(PreviewPaneMinWidth, Math.Min(PreviewPaneMaxWidth, availableForPreview));
                previewWidth = ClampPaneWidth(_previewPaneWidth, PreviewPaneMinWidth, previewMax);
            }

            SidebarPaneColumn.Width = new GridLength(sidebarWidth);
            SidebarSplitterColumn.Width = new GridLength(WorkspaceSplitterWidth);
            PreviewSplitterColumn.Width = hasPreview
                ? new GridLength(WorkspaceSplitterWidth)
                : new GridLength(0);
            PreviewPaneColumn.Width = hasPreview
                ? new GridLength(previewWidth)
                : new GridLength(0);
            FilePaneColumn.Width = new GridLength(1, GridUnitType.Star);
        }
        finally
        {
            _isApplyingWorkspaceSplitLayout = false;
        }
    }

    private void CaptureWorkspaceSplitWidths()
    {
        if (!ViewModel.IsWorkspaceVisible || !_workspaceSplitLoaded)
        {
            return;
        }

        var sidebarActual = SidebarPaneColumn.ActualWidth;
        if (double.IsFinite(sidebarActual) && sidebarActual > 0)
        {
            _sidebarPaneWidth = ClampPaneWidth(sidebarActual, SidebarPaneMinWidth, SidebarPaneMaxWidth);
        }

        if (ViewModel.IsPreviewPaneVisible)
        {
            var previewActual = PreviewPaneColumn.ActualWidth;
            if (double.IsFinite(previewActual) && previewActual > 0)
            {
                _previewPaneWidth = ClampPaneWidth(previewActual, PreviewPaneMinWidth, PreviewPaneMaxWidth);
            }
        }

        SaveWorkspaceUiState();
    }

    private double EffectiveSidebarPaneMaxWidth()
    {
        var totalWidth = WorkspaceSplitGrid.ActualWidth;
        if (!double.IsFinite(totalWidth) || totalWidth <= 0)
        {
            return SidebarPaneMaxWidth;
        }

        var previewWidth = ViewModel.IsPreviewPaneVisible ? Math.Max(PreviewPaneMinWidth, _previewPaneWidth) : 0;
        var dividerWidth = WorkspaceSplitterWidth * (ViewModel.IsPreviewPaneVisible ? 2 : 1);
        return Math.Max(SidebarPaneMinWidth, Math.Min(SidebarPaneMaxWidth, totalWidth - FilePaneMinWidth - previewWidth - dividerWidth));
    }

    private double EffectivePreviewPaneMaxWidth()
    {
        var totalWidth = WorkspaceSplitGrid.ActualWidth;
        if (!double.IsFinite(totalWidth) || totalWidth <= 0)
        {
            return PreviewPaneMaxWidth;
        }

        var dividerWidth = WorkspaceSplitterWidth * 2;
        return Math.Max(PreviewPaneMinWidth, Math.Min(PreviewPaneMaxWidth, totalWidth - _sidebarPaneWidth - FilePaneMinWidth - dividerWidth));
    }

    private void StartWorkspaceSplitDrag(WorkspaceSplitPane pane, UIElement splitter, PointerRoutedEventArgs e)
    {
        EnsureWorkspaceSplitStateLoaded();
        var pointer = e.GetCurrentPoint(WorkspaceSplitGrid);
        _activeSplitDrag = new WorkspacePaneSplitDrag(
            pane,
            pointer.Position.X,
            pane == WorkspaceSplitPane.Sidebar ? SidebarPaneColumn.ActualWidth : PreviewPaneColumn.ActualWidth
        );
        splitter.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void EndWorkspaceSplitDrag(UIElement splitter, PointerRoutedEventArgs e)
    {
        if (_activeSplitDrag is null)
        {
            return;
        }

        _activeSplitDrag = null;
        splitter.ReleasePointerCapture(e.Pointer);
        SetWorkspaceSplitterLine(splitter, false);
        CaptureWorkspaceSplitWidths();
        e.Handled = true;
    }

    private void SetWorkspaceSplitterLine(object? splitter, bool isVisible)
    {
        var brush = isVisible
            ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 203, 213, 225))
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        if (ReferenceEquals(splitter, SidebarPaneSplitter))
        {
            SidebarPaneSplitterLine.Background = brush;
        }
        else if (ReferenceEquals(splitter, PreviewPaneSplitter))
        {
            PreviewPaneSplitterLine.Background = brush;
        }
    }

    private DirectoryListColumn? ColumnForDirectorySplitter(UIElement splitter)
    {
        if (ReferenceEquals(splitter, DirectoryNameColumnSplitter))
        {
            return DirectoryListColumn.Name;
        }

        if (ReferenceEquals(splitter, DirectoryTypeColumnSplitter))
        {
            return DirectoryListColumn.Type;
        }

        if (ReferenceEquals(splitter, DirectorySizeColumnSplitter))
        {
            return DirectoryListColumn.Size;
        }

        return null;
    }

    private double DirectoryColumnWidth(DirectoryListColumn column)
    {
        var definition = DirectoryColumnDefinition(column);
        return definition.ActualWidth > 0 ? definition.ActualWidth : definition.Width.Value;
    }

    private void SetDirectoryColumnWidth(DirectoryListColumn column, double width)
    {
        var definition = DirectoryColumnDefinition(column);
        var maximum = Math.Max(MinimumDirectoryColumnWidth(column), DirectoryItemsListView.ActualWidth - ReservedDirectoryColumnWidth(column));
        var target = ClampPaneWidth(width, MinimumDirectoryColumnWidth(column), maximum);
        definition.Width = new GridLength(target);
    }

    private double ReservedDirectoryColumnWidth(DirectoryListColumn resizingColumn)
    {
        var reserved = 0d;
        foreach (var column in new[] { DirectoryListColumn.Name, DirectoryListColumn.Type, DirectoryListColumn.Size, DirectoryListColumn.Modified })
        {
            if (column == resizingColumn)
            {
                continue;
            }

            reserved += MinimumDirectoryColumnWidth(column);
        }

        return reserved + 42;
    }

    private ColumnDefinition DirectoryColumnDefinition(DirectoryListColumn column) =>
        column switch
        {
            DirectoryListColumn.Name => DirectoryNameColumn,
            DirectoryListColumn.Type => DirectoryTypeColumn,
            DirectoryListColumn.Size => DirectorySizeColumn,
            _ => DirectoryModifiedColumn,
        };

    private static double MinimumDirectoryColumnWidth(DirectoryListColumn column) =>
        column switch
        {
            DirectoryListColumn.Name => DirectoryNameMinWidth,
            DirectoryListColumn.Type => DirectoryTypeMinWidth,
            DirectoryListColumn.Size => DirectorySizeMinWidth,
            _ => DirectoryModifiedMinWidth,
        };

    private void ApplyDirectoryColumnLayout()
    {
        if (_isApplyingDirectoryColumns || _activeDirectoryColumnDrag is not null)
        {
            return;
        }

        var totalWidth = DirectoryContentGrid.ActualWidth - 34;
        if (!double.IsFinite(totalWidth) || totalWidth <= 0)
        {
            return;
        }

        _isApplyingDirectoryColumns = true;
        try
        {
            var widths = FitDirectoryColumnWidths(totalWidth);
            DirectoryNameColumn.Width = new GridLength(widths.Name);
            DirectoryTypeColumn.Width = new GridLength(widths.Type);
            DirectorySizeColumn.Width = new GridLength(widths.Size);
            DirectoryModifiedColumn.Width = new GridLength(widths.Modified);
        }
        finally
        {
            _isApplyingDirectoryColumns = false;
        }
    }

    private (double Name, double Type, double Size, double Modified) FitDirectoryColumnWidths(double totalWidth)
    {
        var name = Math.Max(DirectoryNameMinWidth, totalWidth * _directoryNameRatio);
        var type = Math.Max(DirectoryTypeMinWidth, totalWidth * _directoryTypeRatio);
        var size = Math.Max(DirectorySizeMinWidth, totalWidth * _directorySizeRatio);
        var modified = Math.Max(DirectoryModifiedMinWidth, totalWidth * _directoryModifiedRatio);
        var total = name + type + size + modified;
        if (total <= totalWidth)
        {
            name += totalWidth - total;
            return (name, type, size, modified);
        }

        var overflow = total - totalWidth;
        var reducibleName = Math.Max(0, name - DirectoryNameMinWidth);
        var nameReduction = Math.Min(overflow, reducibleName);
        name -= nameReduction;
        overflow -= nameReduction;
        if (overflow > 0)
        {
            modified = Math.Max(DirectoryModifiedMinWidth, modified - overflow);
        }

        return (name, type, size, modified);
    }

    private void CaptureDirectoryColumnRatios()
    {
        var total = DirectoryNameColumn.ActualWidth
            + DirectoryTypeColumn.ActualWidth
            + DirectorySizeColumn.ActualWidth
            + DirectoryModifiedColumn.ActualWidth;
        if (!double.IsFinite(total) || total <= 0)
        {
            return;
        }

        _directoryNameRatio = DirectoryNameColumn.ActualWidth / total;
        _directoryTypeRatio = DirectoryTypeColumn.ActualWidth / total;
        _directorySizeRatio = DirectorySizeColumn.ActualWidth / total;
        _directoryModifiedRatio = DirectoryModifiedColumn.ActualWidth / total;
    }

    private static double ClampPaneWidth(double width, double minimum, double maximum)
    {
        if (!double.IsFinite(width) || width <= 0)
        {
            return minimum;
        }

        return Math.Max(minimum, Math.Min(width, maximum));
    }

    private static WorkspaceUiState? LoadWorkspaceUiState()
    {
        try
        {
            var path = WorkspaceUiStatePath();
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<WorkspaceUiState>(json);
        }
        catch
        {
            return null;
        }
    }

    private void SaveWorkspaceUiState()
    {
        try
        {
            var path = WorkspaceUiStatePath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var state = new WorkspaceUiState(_sidebarPaneWidth, _previewPaneWidth);
            File.WriteAllText(path, JsonSerializer.Serialize(state));
        }
        catch
        {
            // UI preferences are best-effort; losing them should not block the client.
        }
    }

    private static string WorkspaceUiStatePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RYNATClient",
            "windows-ui.json"
        );

    private void ClearPasswordInput()
    {
        if (PasswordInput.Password.Length == 0)
        {
            return;
        }

        PasswordInput.Password = string.Empty;
    }

    private async Task UpdatePreviewContentAsync()
    {
        if (_isClosed)
        {
            return;
        }

        var previewPane = ViewModel.PreviewPane;
        var renderVersion = Interlocked.Increment(ref _previewRenderVersion);

        if (!string.IsNullOrWhiteSpace(previewPane.LocalVideoPath)
            && string.Equals(_lastPreviewVideoPath, previewPane.LocalVideoPath, StringComparison.OrdinalIgnoreCase)
            && PreviewImage.Source is not null)
        {
            PreviewImageContainer.Visibility = Visibility.Visible;
            PreviewFallbackIcon.Visibility = Visibility.Collapsed;
            await UpdateVideoPreviewMetadataAsync(previewPane.LocalVideoPath, renderVersion);
            return;
        }

        if (!string.IsNullOrWhiteSpace(previewPane.LocalImagePath)
            && string.Equals(_lastPreviewImagePath, previewPane.LocalImagePath, StringComparison.OrdinalIgnoreCase)
            && PreviewImage.Source is not null)
        {
            PreviewImageContainer.Visibility = Visibility.Visible;
            PreviewFallbackIcon.Visibility = Visibility.Collapsed;
            await UpdateImagePreviewMetadataAsync(previewPane.LocalImagePath, renderVersion);
            return;
        }

        if (!string.IsNullOrWhiteSpace(previewPane.LocalPdfPath)
            && string.Equals(_lastPreviewPdfPath, previewPane.LocalPdfPath, StringComparison.OrdinalIgnoreCase)
            && PreviewImage.Source is not null)
        {
            PreviewImageContainer.Visibility = Visibility.Visible;
            PreviewFallbackIcon.Visibility = Visibility.Collapsed;
            await UpdatePdfPreviewMetadataAsync(previewPane.LocalPdfPath, renderVersion);
            return;
        }

        ResetPreviewMedia(clearCacheKeys: true);
        PreviewFallbackIcon.Visibility = Visibility.Visible;

        if (!string.IsNullOrWhiteSpace(previewPane.LocalVideoPath))
        {
            var loaded = await TryLoadVideoPreviewAsync(previewPane.LocalVideoPath, renderVersion);
            if (_isClosed || renderVersion != _previewRenderVersion)
            {
                return;
            }
            if (loaded)
            {
                PreviewImageContainer.Visibility = Visibility.Visible;
                PreviewFallbackIcon.Visibility = Visibility.Collapsed;
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(previewPane.LocalImagePath))
        {
            try
            {
                if (_isClosed || renderVersion != _previewRenderVersion)
                {
                    return;
                }
                PreviewImage.Source = new BitmapImage(new Uri(previewPane.LocalImagePath));
                PreviewImageContainer.Visibility = Visibility.Visible;
                PreviewFallbackIcon.Visibility = Visibility.Collapsed;
                await UpdateImagePreviewMetadataAsync(previewPane.LocalImagePath, renderVersion);
                _lastPreviewImagePath = previewPane.LocalImagePath;
                _lastPreviewPdfPath = null;
                _lastPreviewVideoPath = null;
                return;
            }
            catch
            {
                _lastPreviewImagePath = null;
            }
        }

        if (!string.IsNullOrWhiteSpace(previewPane.LocalPdfPath))
        {
            var rendered = await TryRenderPdfPreviewAsync(previewPane.LocalPdfPath, renderVersion);
            if (_isClosed || renderVersion != _previewRenderVersion)
            {
                return;
            }
            if (rendered)
            {
                PreviewImageContainer.Visibility = Visibility.Visible;
                PreviewFallbackIcon.Visibility = Visibility.Collapsed;
                return;
            }
        }
    }

    private async Task UpdateImagePreviewMetadataAsync(string localImagePath, int renderVersion)
    {
        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(localImagePath);
            using var stream = await storageFile.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            if (_isClosed || renderVersion != _previewRenderVersion)
            {
                return;
            }

            ViewModel.UpdatePreviewImageMetadata(
                localImagePath,
                decoder.PixelWidth,
                decoder.PixelHeight
            );
        }
        catch
        {
        }
    }

    private async Task UpdateVideoPreviewMetadataAsync(string localVideoPath, int renderVersion)
    {
        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(localVideoPath);
            var videoSize = await ReadVideoPixelSizeAsync(storageFile);
            if (_isClosed || renderVersion != _previewRenderVersion)
            {
                return;
            }

            if (videoSize.Width > 0 && videoSize.Height > 0)
            {
                ViewModel.UpdatePreviewVideoMetadata(localVideoPath, videoSize.Width, videoSize.Height);
            }
        }
        catch
        {
        }
    }

    private async Task UpdatePdfPreviewMetadataAsync(string localPdfPath, int renderVersion)
    {
        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(localPdfPath);
            var document = await PdfDocument.LoadFromFileAsync(storageFile);
            if (_isClosed || renderVersion != _previewRenderVersion || document.PageCount == 0)
            {
                return;
            }

            using var page = document.GetPage(0);
            var pageSize = page.Size;
            ViewModel.UpdatePreviewPdfMetadata(
                localPdfPath,
                document.PageCount,
                (uint)Math.Max(0, Math.Round(pageSize.Width)),
                (uint)Math.Max(0, Math.Round(pageSize.Height))
            );
        }
        catch
        {
        }
    }

    private void ResetPreviewMedia(bool clearCacheKeys = false)
    {
        PreviewImage.Source = null;
        PreviewImageContainer.Visibility = Visibility.Collapsed;
        PreviewFallbackIcon.Visibility = Visibility.Visible;

        if (clearCacheKeys)
        {
            _lastPreviewImagePath = null;
            _lastPreviewPdfPath = null;
            _lastPreviewVideoPath = null;
        }
    }

    private async Task<bool> TryLoadVideoPreviewAsync(string localVideoPath, int renderVersion)
    {
        if (_isClosed || string.IsNullOrWhiteSpace(localVideoPath))
        {
            return false;
        }

        if (string.Equals(_lastPreviewVideoPath, localVideoPath, StringComparison.OrdinalIgnoreCase)
            && PreviewImage.Source is not null)
        {
            return true;
        }

        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(localVideoPath);
            if (_isClosed || renderVersion != _previewRenderVersion)
            {
                return false;
            }

            var videoSize = await ReadVideoPixelSizeAsync(storageFile);

            using var thumbnail = await RenderVideoPosterAsync(storageFile);
            if (_isClosed || renderVersion != _previewRenderVersion)
            {
                return false;
            }

            if (thumbnail is not null)
            {
                var poster = new BitmapImage();
                await poster.SetSourceAsync(thumbnail);
                if (_isClosed || renderVersion != _previewRenderVersion)
                {
                    return false;
                }

                PreviewImage.Source = poster;
            }
            else
            {
                return false;
            }

            _lastPreviewVideoPath = localVideoPath;
            _lastPreviewImagePath = null;
            _lastPreviewPdfPath = null;
            if (videoSize.Width > 0 && videoSize.Height > 0)
            {
                ViewModel.UpdatePreviewVideoMetadata(localVideoPath, videoSize.Width, videoSize.Height);
            }
            return true;
        }
        catch
        {
            PreviewImage.Source = null;
            _lastPreviewVideoPath = null;
            return false;
        }
    }

    private static async Task<IRandomAccessStream?> RenderVideoPosterAsync(StorageFile storageFile)
    {
        var clip = await MediaClip.CreateFromFileAsync(storageFile);
        var composition = new MediaComposition();
        composition.Clips.Add(clip);

        var encodingProperties = clip.GetVideoEncodingProperties();
        var sourceWidth = Math.Max(1, (int)encodingProperties.Width);
        var sourceHeight = Math.Max(1, (int)encodingProperties.Height);
        var scale = Math.Min(1.0, 512.0 / Math.Max(sourceWidth, sourceHeight));
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        var time = TimeSpan.FromMilliseconds(100);
        if (composition.Duration > TimeSpan.Zero && composition.Duration < time)
        {
            time = TimeSpan.Zero;
        }

        return await composition.GetThumbnailAsync(
            time,
            width,
            height,
            VideoFramePrecision.NearestFrame
        );
    }

    private static async Task<(uint Width, uint Height)> ReadVideoPixelSizeAsync(StorageFile storageFile)
    {
        try
        {
            var properties = await storageFile.Properties.GetVideoPropertiesAsync();
            return (properties.Width, properties.Height);
        }
        catch
        {
            return (0, 0);
        }
    }

    private async Task<bool> TryRenderPdfPreviewAsync(string localPdfPath, int renderVersion)
    {
        if (_isClosed || string.IsNullOrWhiteSpace(localPdfPath))
        {
            return false;
        }

        if (string.Equals(_lastPreviewPdfPath, localPdfPath, StringComparison.OrdinalIgnoreCase)
            && PreviewImage.Source is not null)
        {
            return true;
        }

        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(localPdfPath);
            var document = await PdfDocument.LoadFromFileAsync(storageFile);
            if (_isClosed || renderVersion != _previewRenderVersion)
            {
                return false;
            }

            if (document.PageCount == 0)
            {
                return false;
            }

            using var page = document.GetPage(0);
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream);
            if (_isClosed || renderVersion != _previewRenderVersion)
            {
                return false;
            }

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            if (_isClosed || renderVersion != _previewRenderVersion)
            {
                return false;
            }

            PreviewImage.Source = bitmap;
            _lastPreviewPdfPath = localPdfPath;
            _lastPreviewImagePath = null;
            _lastPreviewVideoPath = null;
            var pageSize = page.Size;
            ViewModel.UpdatePreviewPdfMetadata(
                localPdfPath,
                document.PageCount,
                (uint)Math.Max(0, Math.Round(pageSize.Width)),
                (uint)Math.Max(0, Math.Round(pageSize.Height))
            );
            return true;
        }
        catch
        {
            _lastPreviewPdfPath = null;
            return false;
        }
    }

    private async Task<string?> PromptForTextAsync(string title, string message, string defaultValue)
    {
        var input = new TextBox
        {
            Text = defaultValue
        };

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    input
                }
            },
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        var value = input.Text.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Contains('/') || value.Contains('\\') || value.Contains(':'))
        {
            await ShowMessageAsync("名称无效", "名称不能为空，且不能包含 /、\\ 或 :");
            return null;
        }

        return value;
    }

    private async Task<IReadOnlyDictionary<string, UploadConflictDecision>?> ResolveUploadConflictsAsync(
        IReadOnlyList<string> localPaths
    )
    {
        var conflicts = await ViewModel.FindUploadConflictsAsync(localPaths);
        var decisions = new Dictionary<string, UploadConflictDecision>(StringComparer.OrdinalIgnoreCase);
        UploadConflictDecision? applyAllDecision = null;

        foreach (var conflict in conflicts)
        {
            var decision = applyAllDecision ?? await PromptUploadConflictAsync(conflict);
            if (decision is null)
            {
                return null;
            }

            decisions[conflict.LocalPath] = decision.Value;
            if (_lastConflictApplyAll)
            {
                applyAllDecision = decision.Value;
            }
        }

        return decisions;
    }

    private async Task<IReadOnlyDictionary<string, UploadConflictDecision>?> ResolvePasteConflictsAsync()
    {
        var conflicts = await ViewModel.FindPasteConflictsAsync();
        var decisions = new Dictionary<string, UploadConflictDecision>(StringComparer.OrdinalIgnoreCase);
        UploadConflictDecision? applyAllDecision = null;

        foreach (var conflict in conflicts)
        {
            var decision = applyAllDecision ?? await PromptPasteConflictAsync(conflict);
            if (decision is null)
            {
                return null;
            }

            decisions[conflict.Source.DisplayPath] = decision.Value;
            if (_lastConflictApplyAll)
            {
                applyAllDecision = decision.Value;
            }
        }

        return decisions;
    }

    private bool _lastConflictApplyAll;

    private async Task<UploadConflictDecision?> PromptUploadConflictAsync(FileUploadConflict conflict)
    {
        _lastConflictApplyAll = false;
        var applyAll = new CheckBox
        {
            Content = "对本次操作全部应用"
        };

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "已存在同名项目",
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"目标位置已存在“{conflict.FileName}”。请选择处理方式。",
                        TextWrapping = TextWrapping.Wrap
                    },
                    applyAll
                }
            },
            PrimaryButtonText = "覆盖",
            SecondaryButtonText = "跳过",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        _lastConflictApplyAll = applyAll.IsChecked == true;

        return result switch
        {
            ContentDialogResult.Primary => UploadConflictDecision.Replace,
            ContentDialogResult.Secondary => UploadConflictDecision.Skip,
            _ => null
        };
    }

    private async Task<UploadConflictDecision?> PromptPasteConflictAsync(FilePasteConflict conflict)
    {
        _lastConflictApplyAll = false;
        var applyAll = new CheckBox
        {
            Content = "对本次操作全部应用"
        };

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "已存在同名项目",
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"目标位置已存在“{conflict.Source.Name}”。请选择处理方式。",
                        TextWrapping = TextWrapping.Wrap
                    },
                    applyAll
                }
            },
            PrimaryButtonText = "覆盖",
            SecondaryButtonText = "跳过",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        _lastConflictApplyAll = applyAll.IsChecked == true;

        return result switch
        {
            ContentDialogResult.Primary => UploadConflictDecision.Replace,
            ContentDialogResult.Secondary => UploadConflictDecision.Skip,
            _ => null
        };
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            CloseButtonText = "知道了",
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }

    private enum WorkspaceSplitPane
    {
        Sidebar,
        Preview
    }

    private enum DirectoryListColumn
    {
        Name,
        Type,
        Size,
        Modified
    }

    private sealed record WorkspacePaneSplitDrag(
        WorkspaceSplitPane Pane,
        double StartX,
        double StartWidth
    );

    private sealed record DirectoryColumnDrag(
        DirectoryListColumn Column,
        double StartX,
        double StartWidth
    );

    private sealed record WorkspaceUiState(
        double SidebarWidth,
        double PreviewWidth
    );

}
