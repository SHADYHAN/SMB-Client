using System.Windows;
using Rynat.Client;
using Rynat.WindowsClient.Platform.Shell;
using Rynat.WindowsClient.Services.Bootstrap;
using Rynat.WindowsClient.Services.Directory;
using Rynat.WindowsClient.Services.Links;
using Rynat.WindowsClient.Services.Preview;
using Rynat.WindowsClient.Services.Smb;
using Rynat.WindowsClient.UI.Shell;

namespace Rynat.WindowsClient;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var bridge = new RynatCoreBridge();
        var bootstrapService = new BootstrapService(bridge);
        var sessionService = new SmbSessionService(bridge);
        var directoryService = new DirectoryService(bridge);
        var quickLinkService = new QuickLinkService(bridge);
        var previewService = new PreviewService(bridge);
        var shellDragDropService = new WindowsShellDragDropService();

        var viewModel = new ShellViewModel(
            bootstrapService,
            sessionService,
            directoryService,
            quickLinkService,
            previewService,
            shellDragDropService
        );

        var window = new MainWindow
        {
            DataContext = viewModel
        };

        MainWindow = window;
        window.Show();
        _ = viewModel.InitializeAsync();
    }
}
