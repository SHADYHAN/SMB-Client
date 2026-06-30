using System.Diagnostics;

namespace Rynat.WindowsTray.Services;

internal sealed class ExplorerService
{
    public void OpenDirectory(string uncPath)
    {
        if (string.IsNullOrWhiteSpace(uncPath))
        {
            return;
        }

        StartExplorer(uncPath);
    }

    public void OpenTarget(string path, string kind)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (string.Equals(kind, "file", StringComparison.OrdinalIgnoreCase))
        {
            StartExplorer($"/select,\"{path}\"");
            return;
        }

        StartExplorer(path);
    }

    private static void StartExplorer(string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = arguments,
            UseShellExecute = true
        });
    }
}
