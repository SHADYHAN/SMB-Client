using System.Text;
using System.Text.Json;

namespace Rynat.WindowsTray.Services;

internal sealed class ShareLinkService
{
    public const int LocalRedirectPort = 19527;

    public string CreateShareLink(string path, string kind)
    {
        var payload = new ShareLinkPayload
        {
            Path = path,
            Kind = string.Equals(kind, "file", StringComparison.OrdinalIgnoreCase) ? "file" : "dir"
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var code = Base64UrlEncode(Encoding.UTF8.GetBytes(json));
        return $"http://127.0.0.1:{LocalRedirectPort}/s/{code}";
    }

    public bool TryDecode(string code, out ShareLinkPayload payload)
    {
        payload = new ShareLinkPayload();

        try
        {
            var json = Encoding.UTF8.GetString(Base64UrlDecode(code));
            var decoded = JsonSerializer.Deserialize<ShareLinkPayload>(json, JsonOptions);
            if (decoded is null || string.IsNullOrWhiteSpace(decoded.Path))
            {
                return false;
            }

            payload = decoded;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal sealed class ShareLinkPayload
{
    public string Path { get; set; } = string.Empty;

    public string Kind { get; set; } = "dir";
}
