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
        try
        {
            StartApplication(e);
        }
        catch (Exception ex)
        {
            LogStartupException(ex);
            MessageBox.Show("启动失败，请重新打开。", "RYNAT 共享网盘", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void StartApplication(StartupEventArgs e)
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

        var bootstrapService = new BootstrapService(bridge);
        var sessionService = new SmbSessionService(bridge);
        var directoryService = new DirectoryService(bridge);
        var remoteCopyMoveService = new RemoteCopyMoveService(bridge);
        var fileOperationService = new FileOperationService(bridge);
        var fileTransferService = new FileTransferService(bridge);
        var quickLinkService = new QuickLinkService(bridge);
        var linkActivationService = new LinkActivationService(bridge);
        var thumbnailService = new WindowsThumbnailService();
        var previewService = new PreviewService(bridge, thumbnailService);
        var serverProfileService = new ServerProfileService(bridge);
        var clipboardService = new WindowsClipboardService();
        var userDialogService = new WindowsUserDialogService();
        var serverSettingsDialogService = new WindowsServerSettingsDialogService(serverProfileService);
        var shellDragDropService = new WindowsShellDragDropService();
        var windowForegroundService = new WindowsWindowForegroundService();

        var viewModel = new ShellViewModel(
            bootstrapService,
            sessionService,
            directoryService,
            fileOperationService,
            remoteCopyMoveService,
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
            ActivateArguments(viewModel, args.Arguments, windowForegroundService);
        };
        _localLinkRedirectService.Activated += (_, args) =>
        {
            ActivateArguments(viewModel, args.Arguments, windowForegroundService);
        };
        _ = _localLinkRedirectService.StartAsync();

        var window = new MainWindow
        {
            DataContext = viewModel
        };

        MainWindow = window;
        window.Show();
        _ = viewModel.InitializeAsync(e.Args);
    }

    private static void LogStartupException(Exception exception)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var directory = System.IO.Path.Combine(appData, "Rynat", "logs");
            System.IO.Directory.CreateDirectory(directory);
            var logPath = System.IO.Path.Combine(directory, "startup.log");
            System.IO.File.AppendAllText(logPath, $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never block application startup or shutdown.
        }
    }

    private void ActivateArguments(
        ShellViewModel viewModel,
        IReadOnlyList<string> arguments,
        IWindowForegroundService windowForegroundService
    )
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (MainWindow is { } activeWindow)
            {
                windowForegroundService.BringToFront(activeWindow);
            }

            _ = viewModel.ActivateExternalArgumentsAsync(arguments);
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _localLinkRedirectService?.Dispose();
        _singleInstanceService?.Dispose();
        base.OnExit(e);
    }
}
