using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rynat.Client;

public sealed record AppBootstrapState(
    [property: JsonPropertyName("server_profiles")]
    StoredServerProfile[] ServerProfiles,
    [property: JsonPropertyName("active_server")]
    StoredServerProfile? ActiveServer,
    [property: JsonPropertyName("active_credential")]
    StoredServerCredential? ActiveCredential
);

public sealed record StoredServerProfile(
    [property: JsonPropertyName("id")]
    string Id,
    [property: JsonPropertyName("display_name")]
    string DisplayName,
    [property: JsonPropertyName("endpoint")]
    StoredServerEndpoint Endpoint,
    [property: JsonPropertyName("username")]
    string? Username,
    [property: JsonPropertyName("auth_mode")]
    string AuthMode,
    [property: JsonPropertyName("dialect_preference")]
    string DialectPreference,
    [property: JsonPropertyName("created_at")]
    string CreatedAt,
    [property: JsonPropertyName("updated_at")]
    string UpdatedAt
);

public sealed record StoredServerEndpoint(
    [property: JsonPropertyName("host")]
    string Host,
    [property: JsonPropertyName("port")]
    ushort? Port
);

public sealed record StoredServerCredential(
    [property: JsonPropertyName("server_profile_id")]
    string ServerProfileId,
    [property: JsonPropertyName("username")]
    string Username,
    [property: JsonPropertyName("remember_password")]
    bool RememberPassword,
    [property: JsonPropertyName("auto_login")]
    bool AutoLogin,
    [property: JsonPropertyName("updated_at")]
    string UpdatedAt
);

public sealed record QuickLink(
    [property: JsonPropertyName("id")]
    string Id,
    [property: JsonPropertyName("target")]
    QuickLinkTarget Target,
    [property: JsonPropertyName("http_url")]
    string HttpUrl,
    [property: JsonPropertyName("deep_link_url")]
    string DeepLinkUrl,
    [property: JsonPropertyName("created_at")]
    string CreatedAt
);

public sealed record QuickLinkTarget(
    [property: JsonPropertyName("server_host")]
    string ServerHost,
    [property: JsonPropertyName("share")]
    string Share,
    [property: JsonPropertyName("path")]
    string Path,
    [property: JsonPropertyName("name")]
    string? Name,
    [property: JsonPropertyName("kind")]
    string Kind
);

public sealed record LinkActivation(
    [property: JsonPropertyName("target")]
    QuickLinkTarget Target,
    [property: JsonPropertyName("matched_server")]
    StoredServerProfile? MatchedServer,
    [property: JsonPropertyName("browse_location")]
    BrowseLocation BrowseLocation,
    [property: JsonPropertyName("preview_plan")]
    PreviewPlan? PreviewPlan
);

public sealed record BrowseLocation(
    [property: JsonPropertyName("server_host")]
    string ServerHost,
    [property: JsonPropertyName("share")]
    string Share,
    [property: JsonPropertyName("remote_path")]
    string RemotePath,
    [property: JsonPropertyName("selected_path")]
    string? SelectedPath
);

public sealed record PreviewPlan(
    [property: JsonPropertyName("target")]
    QuickLinkTarget Target,
    [property: JsonPropertyName("content_type")]
    string ContentType,
    [property: JsonPropertyName("cache_key")]
    string CacheKey,
    [property: JsonPropertyName("max_edge_px")]
    uint MaxEdgePx,
    [property: JsonPropertyName("thumbnail")]
    PreviewAsset? Thumbnail,
    [property: JsonPropertyName("playback")]
    PreviewAsset? Playback
);

public sealed record PreviewAsset(
    [property: JsonPropertyName("kind")]
    string Kind,
    [property: JsonPropertyName("url")]
    string Url,
    [property: JsonPropertyName("cache_key")]
    string CacheKey,
    [property: JsonPropertyName("width")]
    uint? Width,
    [property: JsonPropertyName("height")]
    uint? Height
);

public sealed record TransferPlan(
    [property: JsonPropertyName("direction")]
    string Direction,
    [property: JsonPropertyName("source")]
    TransferEndpoint Source,
    [property: JsonPropertyName("destination")]
    TransferEndpoint Destination,
    [property: JsonPropertyName("buffer_bytes")]
    uint BufferBytes,
    [property: JsonPropertyName("requires_streaming")]
    bool RequiresStreaming,
    [property: JsonPropertyName("allow_ui_memory_copy")]
    bool AllowUIMemoryCopy
);

[
    JsonConverter(typeof(TransferEndpointJsonConverter))
]
public sealed record TransferEndpoint
{
    public TransferEndpoint(QuickLinkTarget? remote = null, LocalFileEndpoint? localFile = null)
    {
        Remote = remote;
        LocalFile = localFile;
    }

    public QuickLinkTarget? Remote { get; }

    public LocalFileEndpoint? LocalFile { get; }
}

public sealed record LocalFileEndpoint(
    [property: JsonPropertyName("path")]
    string Path
);

internal sealed class TransferEndpointJsonConverter : JsonConverter<TransferEndpoint>
{
    public override TransferEndpoint Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.TryGetProperty("remote", out var remoteElement))
        {
            return new TransferEndpoint(
                remoteElement.Deserialize<QuickLinkTarget>(options)
                    ?? throw new JsonException("Invalid remote transfer endpoint"),
                null
            );
        }
        if (root.TryGetProperty("local_file", out var localElement))
        {
            return new TransferEndpoint(
                null,
                localElement.Deserialize<LocalFileEndpoint>(options)
                    ?? throw new JsonException("Invalid local transfer endpoint")
            );
        }

        throw new JsonException("Unknown transfer endpoint");
    }

    public override void Write(
        Utf8JsonWriter writer,
        TransferEndpoint value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartObject();
        if (value.Remote is not null)
        {
            writer.WritePropertyName("remote");
            JsonSerializer.Serialize(writer, value.Remote, options);
        }
        else if (value.LocalFile is not null)
        {
            writer.WritePropertyName("local_file");
            JsonSerializer.Serialize(writer, value.LocalFile, options);
        }
        else
        {
            throw new JsonException("Transfer endpoint has no value");
        }
        writer.WriteEndObject();
    }
}

public sealed record SmbConnectResult(
    [property: JsonPropertyName("connection_id")]
    string ConnectionId,
    [property: JsonPropertyName("host")]
    string Host,
    [property: JsonPropertyName("dialect_label")]
    string DialectLabel,
    [property: JsonPropertyName("shares")]
    SmbShare[] Shares
);

public sealed record SmbShare(
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("comment")]
    string Comment
);

public sealed record SmbFileItem(
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("path")]
    string Path,
    [property: JsonPropertyName("is_dir")]
    bool IsDir,
    [property: JsonPropertyName("size")]
    ulong Size,
    [property: JsonPropertyName("modified_time")]
    long? ModifiedTime
);

public sealed record SmbCachedFile(
    [property: JsonPropertyName("local_path")]
    string LocalPath,
    [property: JsonPropertyName("size")]
    ulong Size
);

public sealed record SmbWriteResult(
    [property: JsonPropertyName("path")]
    string Path,
    [property: JsonPropertyName("size")]
    ulong Size,
    [property: JsonPropertyName("copy_method")]
    string? CopyMethod,
    [property: JsonPropertyName("copy_fallback_reason")]
    string? CopyFallbackReason
);

public sealed record SmbDiagnostics(
    [property: JsonPropertyName("connected")]
    bool Connected,
    [property: JsonPropertyName("connection_id")]
    string? ConnectionId,
    [property: JsonPropertyName("host")]
    string? Host,
    [property: JsonPropertyName("cached_share_count")]
    int CachedShareCount,
    [property: JsonPropertyName("last_copy_method")]
    string? LastCopyMethod,
    [property: JsonPropertyName("last_copy_fallback_reason")]
    string? LastCopyFallbackReason
);

internal sealed record EmptyRequest;
