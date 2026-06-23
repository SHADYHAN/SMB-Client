using System.ComponentModel;
using System.Windows;
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
}
