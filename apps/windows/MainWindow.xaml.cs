using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Rynat.WindowsClient.UI.Shell;

namespace Rynat.WindowsClient;

public partial class MainWindow : Window
{
    private ShellViewModel? _shell;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_shell is not null)
        {
            _shell.Preview.PropertyChanged -= OnPreviewPropertyChanged;
        }

        _shell = e.NewValue as ShellViewModel;
        if (_shell is not null)
        {
            _shell.Preview.PropertyChanged += OnPreviewPropertyChanged;
            UpdatePreviewColumn();
        }
    }

    private void OnPreviewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_shell.Preview.IsVisible))
        {
            UpdatePreviewColumn();
        }
    }

    private void UpdatePreviewColumn()
    {
        if (_shell is null)
        {
            return;
        }

        PreviewColumn.Width = _shell.Preview.IsVisible
            ? new GridLength(320)
            : new GridLength(0);
    }

    public void FocusWorkspaceSearch()
    {
        WorkspaceSearchBox.Focus();
        WorkspaceSearchBox.SelectAll();
    }

    private void WorkspaceSearchBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape || _shell is null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(_shell.FileList.SearchText))
        {
            _shell.FileList.SearchText = string.Empty;
            e.Handled = true;
        }
    }

    private void HeaderUserMenuButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null)
        {
            return;
        }

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
        e.Handled = true;
    }
}
