using System.Text.Json;

namespace Rynat.WindowsClient.Services.Smb;

public interface ISmbTaskService
{
    Task<JsonElement?> RunAsync(
        string operation,
        JsonElement payload,
        string? operationId = null,
        string? serverProfileId = null,
        bool useIsolatedConnection = false,
        CancellationToken cancellationToken = default
    );
}
