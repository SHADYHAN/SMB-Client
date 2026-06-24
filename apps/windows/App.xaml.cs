using System.Windows;
using Rynat.Client;
using Rynat.WindowsClient.Platform.Activation;
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
    private IAppSingleInstanceService? _singleInstanceService;
    private ILocalLinkRedirectService? _localLinkRedirectService;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceService = new WindowsSingleInstanceService();
        var isPrimary = _singleInstanceService.StartAsync(e.Args).GetAwaiter().GetResult();
        if (!isPrimary)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var bridge = new RynatCoreBridge();
        var protocolRegistrationService = new WindowsProtocolRegistrationService();
        protocolRegistrationService.EnsureRegistered();
        _localLinkRedirectService = new LocalLinkRedirectService(bridge);
        _ = _localLinkRedirectService.StartAsync();

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
        var serverSettingsDialogService = new WindowsServerSettingsDialogService(serverProfileService);
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
            serverSettingsDialogService,
            shellDragDropService
        );

        _singleInstanceService.Activated += (_, args) =>
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                if (MainWindow is { } activeWindow)
                {
                    activeWindow.Show();
                    if (activeWindow.WindowState == WindowState.Minimized)
                    {
                        activeWindow.WindowState = WindowState.Normal;
                    }
                    activeWindow.Activate();
                }

                await viewModel.ActivateExternalArgumentsAsync(args.Arguments);
            });
        };

        var window = new MainWindow
        {
            DataContext = viewModel
        };

        MainWindow = window;
        window.Show();
        _ = viewModel.InitializeAsync(e.Args);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _localLinkRedirectService?.Dispose();
        _singleInstanceService?.Dispose();
        base.OnExit(e);
    }
}
