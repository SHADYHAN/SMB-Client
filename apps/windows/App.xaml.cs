using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Rynat.WindowsClient.AppServices;
using Rynat.WindowsClient.PlatformIntegration.Links;
using Rynat.WindowsClient.UI.Main;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace Rynat.WindowsClient;

public partial class App : Application
{
    private const int ShowWindowHide = 0;

    private readonly WindowsSingleInstanceManager _singleInstanceManager;
    private Window? _window;
    private MainShellViewModel? _viewModel;
    private WindowsLocalRedirectServer? _redirectServer;
    private SingleInstanceCommand? _pendingSingleInstanceCommand;

    public App()
    {
        InitializeComponent();
        Services = new WindowsAppContext();
        _singleInstanceManager = Services.SingleInstanceManager;
    }

    public WindowsAppContext Services { get; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var helperMode = arguments.Any(argument => string.Equals(argument, "--redirect-helper", StringComparison.OrdinalIgnoreCase));
        var unregisterLinksMode = arguments.Any(argument => string.Equals(argument, "--unregister-links", StringComparison.OrdinalIgnoreCase));
        var rawLink = TryExtractRawLink(arguments);

        if (unregisterLinksMode)
        {
            Services.Diagnostics.Info("开始清理 Windows 链接注册。");
            Services.LinkPlatformRegistrationService.RemoveRedirectHelperAutoStart();
            Services.LinkPlatformRegistrationService.RemoveProtocolRegistration();
            Exit();
            return;
        }

        if (helperMode)
        {
            Services.Diagnostics.Info("本地链接助手启动。");
            Services.LinkPlatformRegistrationService.EnsureProtocolRegistration(Services.ClientExecutablePath);
            _redirectServer = Services.CreateRedirectServer();
            if (!_redirectServer.Start())
            {
                _redirectServer.Dispose();
                _redirectServer = null;
                Exit();
            }
            _window = new Window
            {
                Title = "RYNAT 链接助手"
            };
            _window.Closed += HelperWindow_Closed;
            _window.Activate();
            HideWindow(_window);
            return;
        }

        var singleInstanceResult = _singleInstanceManager.TryBecomePrimary();
        if (singleInstanceResult == WindowsSingleInstanceStartupResult.Secondary)
        {
            var command = rawLink is null
                ? SingleInstanceCommand.ActivateWindow()
                : SingleInstanceCommand.OpenLink(rawLink);
            var forwardTask = _singleInstanceManager.SendToPrimaryAsync(command);
            if (forwardTask.Wait(TimeSpan.FromSeconds(3)) && forwardTask.Result)
            {
                Environment.Exit(0);
                return;
            }

            Services.Diagnostics.Info("未能确认主客户端已接收单实例命令，当前进程将打开独立窗口以避免链接静默丢失。");
        }

        Services.Diagnostics.Info("Windows 客户端启动。");
        if (singleInstanceResult == WindowsSingleInstanceStartupResult.Primary)
        {
            _singleInstanceManager.CommandReceived += HandleSingleInstanceCommandAsync;
        }
        else
        {
            Services.Diagnostics.Info("单实例 IPC 不可用，当前窗口仍将正常启动。");
        }
        Services.LinkPlatformRegistrationService.EnsureProtocolRegistration(Services.ClientExecutablePath);
        Services.LinkPlatformRegistrationService.EnsureRedirectHelperAutoStart(Services.ClientExecutablePath);
        Services.LinkPlatformRegistrationService.StartRedirectHelper(Services.ClientExecutablePath);

        _viewModel = new MainShellViewModel(
            Services.BootstrapService,
            Services.ServerProfileManagementService,
            Services.SmbSessionService,
            Services.DirectoryBrowserService,
            Services.FileOpenService,
            Services.FileDownloadService,
            Services.FileDragDownloadPreparationService,
            Services.FileBatchOperationService,
            Services.FileWriteService,
            Services.FileTaskService,
            Services.LinkActivationService,
            Services.LinkShareService,
            Services.QuickLinkLibraryService,
            Services.PreviewEntryService,
            Services.Diagnostics
        );

        _window = new MainWindow(_viewModel);
        _window.Activate();
        if (_pendingSingleInstanceCommand is not null)
        {
            var pendingCommand = _pendingSingleInstanceCommand;
            _pendingSingleInstanceCommand = null;
            _ = HandleSingleInstanceCommandAsync(pendingCommand);
        }

        if (!string.IsNullOrWhiteSpace(rawLink))
        {
            _viewModel.HandleExternalLinkReceived(rawLink);
        }
    }

    private Task HandleSingleInstanceCommandAsync(SingleInstanceCommand command)
    {
        if (_window is not MainWindow mainWindow)
        {
            _pendingSingleInstanceCommand = command;
            return Task.CompletedTask;
        }

        return EnqueueOnWindowAsync(mainWindow.DispatcherQueue, () =>
        {
            Services.Diagnostics.Info($"主窗口收到单实例命令：{command.Kind}");
            _window?.Activate();
            if (command.Kind == "open_link" && !string.IsNullOrWhiteSpace(command.RawLink))
            {
                _viewModel?.HandleExternalLinkReceived(command.RawLink);
            }
        });
    }

    private static Task EnqueueOnWindowAsync(DispatcherQueue dispatcherQueue, Action action)
    {
        var completion = new TaskCompletionSource();
        if (!dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }))
        {
            completion.SetException(new InvalidOperationException("无法切回主窗口线程处理单实例命令。"));
        }

        return completion.Task;
    }

    private void HelperWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_window is not null)
        {
            _window.Closed -= HelperWindow_Closed;
        }

        _redirectServer?.Dispose();
        _redirectServer = null;
        Exit();
    }

    private static void HideWindow(Window window)
    {
        var handle = WindowNative.GetWindowHandle(window);
        if (handle != IntPtr.Zero)
        {
            ShowWindow(handle, ShowWindowHide);
        }
    }

    private static string? TryExtractRawLink(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            var current = arguments[index];
            if (string.Equals(current, "--open-link", StringComparison.OrdinalIgnoreCase))
            {
                return index + 1 < arguments.Count ? arguments[index + 1] : null;
            }

            if (current.StartsWith("rynat://", StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }
        }

        return null;
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
