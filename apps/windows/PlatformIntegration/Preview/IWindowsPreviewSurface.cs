using Rynat.Client;
using Rynat.WindowsClient.AppServices.Preview;

namespace Rynat.WindowsClient.PlatformIntegration.Preview;

public interface IWindowsPreviewSurface
{
    PreviewPaneState BuildPlaceholder(string title, string description);

    PreviewPaneState BuildFromPlan(PreviewPlan? plan, string remotePath);
}
