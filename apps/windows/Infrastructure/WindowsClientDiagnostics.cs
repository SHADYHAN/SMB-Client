using System;
using System.IO;
using System.Text;

namespace Rynat.WindowsClient.Infrastructure;

public sealed class WindowsClientDiagnostics
{
    private readonly string _logPath;
    private readonly object _syncRoot = new();

    public WindowsClientDiagnostics()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logDirectory = Path.Combine(appData, "Rynat", "Logs");
        Directory.CreateDirectory(logDirectory);
        _logPath = Path.Combine(logDirectory, "windows-client.log");
    }

    public string LogPath => _logPath;

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Error(string message)
    {
        Write("ERROR", message);
    }

    public void Error(Exception exception, string context)
    {
        Write("ERROR", $"{context}: {exception.GetType().Name}: {exception.Message}");
    }

    private void Write(string level, string message)
    {
        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
        lock (_syncRoot)
        {
            File.AppendAllText(_logPath, line, Encoding.UTF8);
        }
    }
}
