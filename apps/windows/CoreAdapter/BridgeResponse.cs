using System.Text.Json.Serialization;

namespace Rynat.Client;

public sealed record BridgeResponse<T>(
    [property: JsonPropertyName("ok")]
    bool Ok,
    [property: JsonPropertyName("data")]
    T? Data,
    [property: JsonPropertyName("error")]
    string? Error,
    [property: JsonPropertyName("error_code")]
    string? ErrorCode
);
