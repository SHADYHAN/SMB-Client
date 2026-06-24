using System.Windows;

namespace Rynat.WindowsClient.Platform.Shell;

public interface IWindowForegroundService
{
    void BringToFront(Window window);
}
