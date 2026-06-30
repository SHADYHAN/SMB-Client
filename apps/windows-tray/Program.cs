using System.Threading;
using System.Windows.Forms;
using Rynat.WindowsTray.App;

namespace Rynat.WindowsTray;

internal static class Program
{
    private const string MutexName = "Global\\Rynat.WindowsTray";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(args));
    }
}
