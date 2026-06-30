using System.Security.Cryptography;

namespace Rynat.WindowsTray.Services;

internal static class ContextIpcSecurity
{
    public const string HeaderName = "X-RYANT-Context-Token";
    private const string TokenFileName = "context-ipc-token";

    public static string GetToken()
    {
        Directory.CreateDirectory(AppDataDirectory);
        if (File.Exists(TokenPath))
        {
            var existing = File.ReadAllText(TokenPath).Trim();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(TokenPath, token);
        return token;
    }

    private static string AppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RYNAT");

    private static string TokenPath => Path.Combine(AppDataDirectory, TokenFileName);
}
