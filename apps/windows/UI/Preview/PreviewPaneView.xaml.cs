using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Rynat.WindowsClient.UI.Preview;

public partial class PreviewPaneView : UserControl
{
    private bool _isVideoPlaying;
    private INotifyPropertyChanged? _previewState;

    public PreviewPaneView()
    {
        InitializeComponent();
        DataContextChanged += PreviewPaneView_OnDataContextChanged;
    }

    private void PreviewPaneView_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_previewState is not null)
        {
            _previewState.PropertyChanged -= PreviewState_OnPropertyChanged;
        }

        _previewState = e.NewValue as INotifyPropertyChanged;
        if (_previewState is not null)
        {
            _previewState.PropertyChanged += PreviewState_OnPropertyChanged;
        }

        ResetVideoState();
    }

    private void PreviewState_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PreviewPaneViewModel.LocalVideoPath))
        {
            ResetVideoState();
        }
    }

    private void ResetVideoState()
    {
        VideoPreview.Stop();
        _isVideoPlaying = false;
        if (DataContext is PreviewPaneViewModel preview)
        {
            preview.IsVideoPlaying = false;
        }

        VideoPlayButton.Content = "播放";
    }

    private void VideoPlayButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isVideoPlaying)
        {
            VideoPreview.Pause();
            _isVideoPlaying = false;
            if (DataContext is PreviewPaneViewModel preview)
            {
                preview.IsVideoPlaying = false;
            }

            VideoPlayButton.Content = "播放";
            return;
        }

        if (DataContext is PreviewPaneViewModel activePreview)
        {
            activePreview.IsVideoPlaying = true;
        }

        VideoPreview.Play();
        _isVideoPlaying = true;
        VideoPlayButton.Content = "暂停";
    }
}
