using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Rynat.WindowsClient.Platform.Shell;

public sealed class WindowsWindowForegroundService : IWindowForegroundService
{
    public void BringToFront(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.ShowInTaskbar = true;
        window.Activate();

        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            ShowWindow(handle, ShowWindowRestore);
            SetForegroundWindow(handle);
        }

        // Windows can refuse foreground activation from a browser callback.
        // A brief topmost toggle gives the user a visible, focused window without keeping it pinned.
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    private const int ShowWindowRestore = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
