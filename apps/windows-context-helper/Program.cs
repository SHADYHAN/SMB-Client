using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace Rynat.WindowsContextHelper;

internal static class Program
{
    private const int ContextIpcPort = 19528;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var request = ContextRequest.FromArgs(args);
            var response = SendContextRequest(request);
            if (!response.Ok)
            {
                ShowError(response.Message ?? "复制分享链接失败。");
                return 1;
            }

            if (!string.IsNullOrWhiteSpace(response.HttpUrl))
            {
                Clipboard.SetText(response.HttpUrl);
            }

            return 0;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            return 1;
        }
    }

    private static ContextResponse SendContextRequest(ContextRequest request)
    {
        using var client = new TcpClient();
        var connectTask = client.ConnectAsync("127.0.0.1", ContextIpcPort);
        try
        {
            if (!connectTask.Wait(TimeSpan.FromMilliseconds(700)))
            {
                throw new InvalidOperationException("RYNAT 未运行，请先打开并登录 RYNAT。");
            }
        }
        catch
        {
            throw new InvalidOperationException("RYNAT 未运行，请先打开并登录 RYNAT。");
        }

        client.ReceiveTimeout = 3000;
        client.SendTimeout = 3000;

        var body = JsonSerializer.Serialize(request, JsonOptions);
        var rawRequest =
            $"POST /context HTTP/1.1\r\nHost: 127.0.0.1:{ContextIpcPort}\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";

        using var stream = client.GetStream();
        var requestBytes = Encoding.UTF8.GetBytes(rawRequest);
        stream.Write(requestBytes);
        client.Client.Shutdown(SocketShutdown.Send);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var rawResponse = reader.ReadToEnd();
        var bodyIndex = rawResponse.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (bodyIndex < 0)
        {
            throw new InvalidOperationException("RYNAT 返回了无效响应。");
        }

        var statusLineEnd = rawResponse.IndexOf("\r\n", StringComparison.Ordinal);
        var statusLine = statusLineEnd < 0 ? rawResponse : rawResponse[..statusLineEnd];
        if (!statusLine.StartsWith("HTTP/1.1 2", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("RYNAT 复制链接请求失败。");
        }

        var responseBody = rawResponse[(bodyIndex + 4)..];
        return JsonSerializer.Deserialize<ContextResponse>(responseBody, JsonOptions)
            ?? throw new InvalidOperationException("RYNAT 返回了空响应。");
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(message, "RYNAT", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}

internal sealed class ContextRequest
{
    public string Action { get; init; } = "copy_link";

    public string Path { get; init; } = string.Empty;

    public string Kind { get; init; } = "file";

    public static ContextRequest FromArgs(IReadOnlyList<string> args)
    {
        if (args.Count < 2 || !string.Equals(args[0], "copy-link", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(@"用法：Rynat.WindowsContextHelper.exe copy-link ""\\host\share\path"" --kind file");
        }

        var kind = "file";
        for (var index = 2; index < args.Count; index++)
        {
            if (!string.Equals(args[index], "--kind", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"不支持的参数：{args[index]}");
            }

            if (index + 1 >= args.Count)
            {
                throw new InvalidOperationException("--kind 缺少参数值。");
            }

            kind = NormalizeKind(args[index + 1]);
            index++;
        }

        var path = args[1].Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("没有收到选中的文件路径。");
        }

        return new ContextRequest
        {
            Path = path,
            Kind = kind
        };
    }

    private static string NormalizeKind(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "file" => "file",
            "dir" or "directory" => "dir",
            _ => throw new InvalidOperationException($"不支持的路径类型：{value}")
        };
    }
}

internal sealed class ContextResponse
{
    public bool Ok { get; init; }

    [JsonPropertyName("http_url")]
    public string? HttpUrl { get; init; }

    public string? Message { get; init; }
}
