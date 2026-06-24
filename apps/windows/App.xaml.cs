using System.Windows;
using Rynat.Client;
using Rynat.WindowsClient.Platform.Clipboard;
using Rynat.WindowsClient.Platform.Dialogs;
using Rynat.WindowsClient.Platform.Shell;
using Rynat.WindowsClient.Services.Bootstrap;
using Rynat.WindowsClient.Services.Directory;
using Rynat.WindowsClient.Services.FileOperations;
using Rynat.WindowsClient.Services.FileTransfers;
using Rynat.WindowsClient.Services.Links;
using Rynat.WindowsClient.Services.LinkActivation;
using Rynat.WindowsClient.Services.Preview;
using Rynat.WindowsClient.Services.Profiles;
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
        var fileOperationService = new FileOperationService(bridge);
        var fileTransferService = new FileTransferService(bridge);
        var quickLinkService = new QuickLinkService(bridge);
        var linkActivationService = new LinkActivationService(bridge);
        var previewService = new PreviewService(bridge);
        var serverProfileService = new ServerProfileService(bridge);
        var clipboardService = new WindowsClipboardService();
        var userDialogService = new WindowsUserDialogService();
        var shellDragDropService = new WindowsShellDragDropService();

        var viewModel = new ShellViewModel(
            bootstrapService,
            sessionService,
            directoryService,
            fileOperationService,
            fileTransferService,
            quickLinkService,
            linkActivationService,
            previewService,
            serverProfileService,
            clipboardService,
            userDialogService,
            shellDragDropService
        );

        var window = new MainWindow
        {
            DataContext = viewModel
        };

        MainWindow = window;
        window.Show();
        _ = viewModel.InitializeAsync(e.Args);
    }
}
