using System.Security.Cryptography;
using System.Text;

namespace Rynat.WindowsClient.Infrastructure;

public static class StableCacheKey
{
    public static string FromParts(params string?[] parts)
    {
        var input = string.Join("\u001f", parts.Select(part => part ?? string.Empty));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes, 0, 12).ToLowerInvariant();
    }
}
