using Rynat.Client;
using Rynat.WindowsClient.AppServices.Bootstrap;
using Rynat.WindowsClient.AppServices.Cache;
using Rynat.WindowsClient.AppServices.Directory;
using Rynat.WindowsClient.AppServices.Files;
using Rynat.WindowsClient.AppServices.Links;
using Rynat.WindowsClient.AppServices.Preview;
using Rynat.WindowsClient.AppServices.Smb;
using Rynat.WindowsClient.AppServices.Tasks;
using Rynat.WindowsClient.Infrastructure;
using Rynat.WindowsClient.PlatformIntegration.Files;
using Rynat.WindowsClient.PlatformIntegration.Links;
using Rynat.WindowsClient.PlatformIntegration.Preview;
using System.IO;
using System.Reflection;

namespace Rynat.WindowsClient.AppServices;

public sealed class WindowsAppContext
{
    public WindowsAppContext()
    {
        Diagnostics = new WindowsClientDiagnostics();
        CoreBridge = new RynatCoreBridge();
        PreviewSurface = new WindowsPreviewSurface();
        FileLauncher = new WindowsFileLauncher();
        FileTaskService = new WindowsFileTaskService(CoreBridge, Diagnostics);
        CacheManagementService = new WindowsCacheManagementService(Diagnostics);
        BootstrapService = new AppBootstrapService(CoreBridge);
        SmbSessionService = new SmbSessionService(CoreBridge);
        ServerProfileManagementService = new ServerProfileManagementService(CoreBridge, Diagnostics);
        DirectoryBrowserService = new DirectoryBrowserService(CoreBridge, Diagnostics);
        FileOpenService = new FileOpenService(CoreBridge, FileLauncher, Diagnostics);
        FileDownloadService = new FileDownloadService(CoreBridge, Diagnostics);
        FileWriteService = new FileWriteService(CoreBridge, Diagnostics);
        FileFolderUploadService = new FileFolderUploadService(CoreBridge, Diagnostics);
        FileBatchOperationService = new FileBatchOperationService(
            FileDownloadService,
            FileWriteService,
            FileFolderUploadService,
            Diagnostics
        );
        FileDragDownloadPreparationService = new FileDragDownloadPreparationService(
            FileDownloadService,
            FileTaskService,
            Diagnostics
        );
        PreviewEntryService = new PreviewEntryService(CoreBridge, PreviewSurface, CacheManagementService);
        LinkActivationService = new LinkActivationService(CoreBridge, PreviewEntryService);
        LinkShareService = new LinkShareService(CoreBridge);
        QuickLinkLibraryService = new QuickLinkLibraryService(CoreBridge, Diagnostics);
        LinkPlatformRegistrationService = new WindowsLinkPlatformRegistrationService(Diagnostics);
        SingleInstanceManager = new WindowsSingleInstanceManager(Diagnostics);
        ClientExecutablePath = ResolveClientExecutablePath();
    }

    public WindowsClientDiagnostics Diagnostics { get; }

    public RynatCoreBridge CoreBridge { get; }

    public AppBootstrapService BootstrapService { get; }

    public SmbSessionService SmbSessionService { get; }

    public ServerProfileManagementService ServerProfileManagementService { get; }

    public DirectoryBrowserService DirectoryBrowserService { get; }

    public FileOpenService FileOpenService { get; }

    public FileDownloadService FileDownloadService { get; }

    public FileWriteService FileWriteService { get; }

    public FileFolderUploadService FileFolderUploadService { get; }

    public FileBatchOperationService FileBatchOperationService { get; }

    public FileDragDownloadPreparationService FileDragDownloadPreparationService { get; }

    public WindowsFileTaskService FileTaskService { get; }

    public WindowsCacheManagementService CacheManagementService { get; }

    public LinkActivationService LinkActivationService { get; }

    public LinkShareService LinkShareService { get; }

    public QuickLinkLibraryService QuickLinkLibraryService { get; }

    public PreviewEntryService PreviewEntryService { get; }

    public IWindowsFileLauncher FileLauncher { get; }

    public IWindowsPreviewSurface PreviewSurface { get; }

    public WindowsLinkPlatformRegistrationService LinkPlatformRegistrationService { get; }

    public WindowsSingleInstanceManager SingleInstanceManager { get; }

    public string ClientExecutablePath { get; }

    public WindowsLocalRedirectServer CreateRedirectServer()
    {
        return new WindowsLocalRedirectServer(CoreBridge, Diagnostics, ClientExecutablePath);
    }

    private static string ResolveClientExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            return processPath;
        }

        var assemblyLocation = Assembly.GetEntryAssembly()?.Location;
        if (string.IsNullOrWhiteSpace(assemblyLocation))
        {
            throw new InvalidOperationException("无法确定 Windows 客户端可执行文件路径。");
        }

        if (string.Equals(Path.GetExtension(assemblyLocation), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            var executablePath = Path.ChangeExtension(assemblyLocation, ".exe");
            if (File.Exists(executablePath))
            {
                return executablePath;
            }
        }

        return assemblyLocation;
    }
}
