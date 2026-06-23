using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rynat.Client;

public sealed record GenerateLinkRequest(
    [property: JsonPropertyName("server_host")]
    string ServerHost,
    [property: JsonPropertyName("share")]
    string Share,
    [property: JsonPropertyName("path")]
    string Path,
    [property: JsonPropertyName("kind")]
    string Kind
);

public sealed record BuildLinkRequest(
    [property: JsonPropertyName("server_host")]
    string ServerHost,
    [property: JsonPropertyName("share")]
    string Share,
    [property: JsonPropertyName("path")]
    string Path,
    [property: JsonPropertyName("kind")]
    string Kind
);

public sealed record ActivateLinkRequest(
    [property: JsonPropertyName("raw_link")]
    string RawLink
);

public sealed record PreviewPlanRequest(
    [property: JsonPropertyName("server_host")]
    string ServerHost,
    [property: JsonPropertyName("share")]
    string Share,
    [property: JsonPropertyName("path")]
    string Path,
    [property: JsonPropertyName("kind")]
    string Kind,
    [property: JsonPropertyName("max_edge_px")]
    uint? MaxEdgePx
);

public sealed record UploadPlanRequest(
    [property: JsonPropertyName("local_path")]
    string LocalPath,
    [property: JsonPropertyName("server_host")]
    string ServerHost,
    [property: JsonPropertyName("share")]
    string Share,
    [property: JsonPropertyName("remote_path")]
    string RemotePath
);

public sealed record RedirectPageRequest(
    [property: JsonPropertyName("target_url")]
    string TargetUrl
);

public sealed record OpenStoreRequest(
    [property: JsonPropertyName("path")]
    string Path
);

public sealed record SaveServerProfileRequest(
    [property: JsonPropertyName("id")]
    string? Id,
    [property: JsonPropertyName("display_name")]
    string DisplayName,
    [property: JsonPropertyName("host")]
    string Host,
    [property: JsonPropertyName("username")]
    string? Username,
    [property: JsonPropertyName("auth_mode")]
    string? AuthMode,
    [property: JsonPropertyName("dialect_preference")]
    string? DialectPreference,
    [property: JsonPropertyName("set_active")]
    bool? SetActive
);

public sealed record SetActiveServerProfileRequest(
    [property: JsonPropertyName("id")]
    string Id
);

public sealed record DeleteServerProfileRequest(
    [property: JsonPropertyName("id")]
    string Id
);

public sealed record SaveServerCredentialRequest(
    [property: JsonPropertyName("server_profile_id")]
    string ServerProfileId,
    [property: JsonPropertyName("username")]
    string Username,
    [property: JsonPropertyName("password")]
    string Password,
    [property: JsonPropertyName("remember_password")]
    bool RememberPassword,
    [property: JsonPropertyName("auto_login")]
    bool AutoLogin
);

public sealed record UpdateServerCredentialOptionsRequest(
    [property: JsonPropertyName("server_profile_id")]
    string ServerProfileId,
    [property: JsonPropertyName("remember_password")]
    bool RememberPassword,
    [property: JsonPropertyName("auto_login")]
    bool AutoLogin
);

public sealed record DeleteServerCredentialRequest(
    [property: JsonPropertyName("server_profile_id")]
    string ServerProfileId
);

public sealed record EncryptCredentialRequest(
    [property: JsonPropertyName("password")]
    string Password
);

public sealed record DecryptCredentialRequest(
    [property: JsonPropertyName("encrypted")]
    string Encrypted
);

public sealed record DeleteQuickLinkRequest(
    [property: JsonPropertyName("id")]
    string Id
);

public sealed record SmbConnectRequest(
    [property: JsonPropertyName("host")]
    string Host,
    [property: JsonPropertyName("username")]
    string Username,
    [property: JsonPropertyName("password")]
    string Password,
    [property: JsonPropertyName("connection_id")]
    string? ConnectionId = null
);

public sealed record SmbConnectStoredCredentialRequest(
    [property: JsonPropertyName("server_profile_id")]
    string ServerProfileId,
    [property: JsonPropertyName("connection_id")]
    string? ConnectionId = null
);

public sealed record SmbListDirectoryRequest(
    [property: JsonPropertyName("share")]
    string Share,
    [property: JsonPropertyName("path")]
    string Path,
    [property: JsonPropertyName("connection_id")]
    string? ConnectionId = null,
    [property: JsonPropertyName("operation_id")]
    string? OperationId = null
);

public sealed record SmbCacheFileRequest(
    [property: JsonPropertyName("share")]
    string Share,
    [property: JsonPropertyName("path")]
    string Path,
    [property: JsonPropertyName("local_path")]
    string LocalPath,
    [property: JsonPropertyName("max_bytes")]
    ulong? MaxBytes = null,
    [property: JsonPropertyName("connection_id")]
    string? ConnectionId = null,
    [property: JsonPropertyName("operation_id")]
    string? OperationId = null
);

public sealed record SmbUploadFileRequest(
    [property: JsonPropertyName("share")]
    string Share,
    [property: JsonPropertyName("local_path")]
    string LocalPath,
    [property: JsonPropertyName("remote_path")]
    string RemotePath,
    [property: JsonPropertyName("replace_existing")]
    bool ReplaceExisting,
    [property: JsonPropertyName("connection_id")]
    string? ConnectionId = null,
    [property: JsonPropertyName("operation_id")]
    string? OperationId = null
);

public sealed record SmbCreateDirectoryRequest(
    [property: JsonPropertyName("share")]
    string Share,
    [property: JsonPropertyName("path")]
    string Path,
    [property: JsonPropertyName("connection_id")]
    string? ConnectionId = null,
    [property: JsonPropertyName("operation_id")]
    string? OperationId = null
);

public sealed record SmbRenameRequest(
    [property: JsonPropertyName("share")]
    string Share,
    [property: JsonPropertyName("from_path")]
    string FromPath,
    [property: JsonPropertyName("to_path")]
    string ToPath,
    [property: JsonPropertyName("connection_id")]
    string? ConnectionId = null,
    [property: JsonPropertyName("operation_id")]
    string? OperationId = null
);

public sealed record SmbCopyFileRequest(
    [property: JsonPropertyName("source_share")]
    string SourceShare,
    [property: JsonPropertyName("source_path")]
    string SourcePath,
    [property: JsonPropertyName("target_share")]
    string TargetShare,
    [property: JsonPropertyName("target_path")]
    string TargetPath,
    [property: JsonPropertyName("replace_existing")]
    bool ReplaceExisting,
    [property: JsonPropertyName("connection_id")]
    string? ConnectionId = null,
    [property: JsonPropertyName("operation_id")]
    string? OperationId = null
);

public sealed record SmbDeleteRequest(
    [property: JsonPropertyName("share")]
    string Share,
    [property: JsonPropertyName("path")]
    string Path,
    [property: JsonPropertyName("is_dir")]
    bool IsDir,
    [property: JsonPropertyName("connection_id")]
    string? ConnectionId = null,
    [property: JsonPropertyName("operation_id")]
    string? OperationId = null
);

public sealed record SmbConnectionScopedRequest(
    [property: JsonPropertyName("connection_id")]
    string? ConnectionId = null
);

public sealed record SmbCancelOperationRequest(
    [property: JsonPropertyName("operation_id")]
    string OperationId
);

public sealed record SmbStartTaskRequest(
    [property: JsonPropertyName("operation")]
    string Operation,
    [property: JsonPropertyName("payload")]
    JsonElement Payload,
    [property: JsonPropertyName("operation_id")]
    string? OperationId = null,
    [property: JsonPropertyName("server_profile_id")]
    string? ServerProfileId = null,
    [property: JsonPropertyName("use_isolated_connection")]
    bool? UseIsolatedConnection = null
);

public sealed record SmbTaskRequest(
    [property: JsonPropertyName("task_id")]
    string TaskId
);

public sealed record SmbTaskStartResult(
    [property: JsonPropertyName("task_id")]
    string TaskId,
    [property: JsonPropertyName("operation_id")]
    string OperationId,
    [property: JsonPropertyName("state")]
    string State
);

public sealed record SmbTaskStatus(
    [property: JsonPropertyName("task_id")]
    string TaskId,
    [property: JsonPropertyName("operation_id")]
    string OperationId,
    [property: JsonPropertyName("operation")]
    string Operation,
    [property: JsonPropertyName("state")]
    string State,
    [property: JsonPropertyName("connection_id")]
    string? ConnectionId,
    [property: JsonPropertyName("started_at_ms")]
    ulong StartedAtMs,
    [property: JsonPropertyName("finished_at_ms")]
    ulong? FinishedAtMs,
    [property: JsonPropertyName("data")]
    JsonElement? Data,
    [property: JsonPropertyName("error")]
    string? Error,
    [property: JsonPropertyName("error_code")]
    string? ErrorCode
);
