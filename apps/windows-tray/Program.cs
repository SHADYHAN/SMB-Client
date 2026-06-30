using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using Rynat.WindowsTray.App;

namespace Rynat.WindowsTray;

internal static class Program
{
    private const string MutexName = "Global\\Rynat.WindowsTray";
    private const string ActivationPipeName = "Rynat.WindowsTray.Activation";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            TryForwardActivation(args);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(args, ActivationPipeName));
    }

    private static void TryForwardActivation(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", ActivationPipeName, PipeDirection.Out);
            client.Connect(700);
            using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
            writer.Write(JsonSerializer.Serialize(args));
        }
        catch
        {
            // If the running instance is still starting, silently exit like a normal single-instance app.
        }
    }
}
