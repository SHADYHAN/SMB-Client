namespace Rynat.WindowsTray.Services;

internal static class UncPathPolicy
{
    public static bool IsPathUnderServer(string path, string serverHost)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(serverHost))
        {
            return false;
        }

        var normalizedPath = NormalizePath(path);
        var serverRoot = $@"\\{serverHost.Trim().Trim('\\', '/')}";
        return normalizedPath.Equals(serverRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(serverRoot + @"\", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().Replace('/', '\\');
    }
}
