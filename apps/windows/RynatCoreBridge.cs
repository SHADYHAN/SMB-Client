using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rynat.Client;

public sealed class RynatCoreBridge
{
    public QuickLink GenerateLink(GenerateLinkRequest request) =>
        Unwrap(Call<GenerateLinkRequest, BridgeResponse<QuickLink>>(
            request,
            NativeMethods.rynat_generate_link_json
        ));

    public QuickLink BuildLink(BuildLinkRequest request) =>
        Unwrap(Call<BuildLinkRequest, BridgeResponse<QuickLink>>(
            request,
            NativeMethods.rynat_build_link_json
        ));

    public LinkActivation ActivateLink(ActivateLinkRequest request) =>
        Unwrap(Call<ActivateLinkRequest, BridgeResponse<LinkActivation>>(
            request,
            NativeMethods.rynat_activate_link_json
        ));

    public PreviewPlan PreviewPlan(PreviewPlanRequest request) =>
        Unwrap(Call<PreviewPlanRequest, BridgeResponse<PreviewPlan>>(
            request,
            NativeMethods.rynat_preview_plan_json
        ));

    public TransferPlan UploadPlan(UploadPlanRequest request) =>
        Unwrap(Call<UploadPlanRequest, BridgeResponse<TransferPlan>>(
            request,
            NativeMethods.rynat_upload_plan_json
        ));

    public string RedirectPage(RedirectPageRequest request) =>
        Unwrap(Call<RedirectPageRequest, BridgeResponse<string>>(
            request,
            NativeMethods.rynat_redirect_page_json
        ));

    public AppBootstrapState OpenStore(OpenStoreRequest request) =>
        Unwrap(Call<OpenStoreRequest, BridgeResponse<AppBootstrapState>>(
            request,
            NativeMethods.rynat_open_store_json
        ));

    public AppBootstrapState AppBootstrap() =>
        Unwrap(Call<EmptyRequest, BridgeResponse<AppBootstrapState>>(
            new EmptyRequest(),
            NativeMethods.rynat_app_bootstrap_json
        ));

    public StoredServerProfile SaveServerProfile(SaveServerProfileRequest request) =>
        Unwrap(Call<SaveServerProfileRequest, BridgeResponse<StoredServerProfile>>(
            request,
            NativeMethods.rynat_save_server_profile_json
        ));

    public AppBootstrapState SetActiveServerProfile(SetActiveServerProfileRequest request) =>
        Unwrap(Call<SetActiveServerProfileRequest, BridgeResponse<AppBootstrapState>>(
            request,
            NativeMethods.rynat_set_active_server_profile_json
        ));

    public AppBootstrapState DeleteServerProfile(DeleteServerProfileRequest request) =>
        Unwrap(Call<DeleteServerProfileRequest, BridgeResponse<AppBootstrapState>>(
            request,
            NativeMethods.rynat_delete_server_profile_json
        ));

    public StoredServerCredential SaveServerCredential(SaveServerCredentialRequest request) =>
        Unwrap(Call<SaveServerCredentialRequest, BridgeResponse<StoredServerCredential>>(
            request,
            NativeMethods.rynat_save_server_credential_json
        ));

    public StoredServerCredential? UpdateServerCredentialOptions(UpdateServerCredentialOptionsRequest request) =>
        UnwrapNullable(Call<UpdateServerCredentialOptionsRequest, BridgeResponse<StoredServerCredential?>>(
            request,
            NativeMethods.rynat_update_server_credential_options_json
        ));

    public void DeleteServerCredential(DeleteServerCredentialRequest request) =>
        Unwrap(Call<DeleteServerCredentialRequest, BridgeResponse<bool>>(
            request,
            NativeMethods.rynat_delete_server_credential_json
        ));

    public string EncryptCredential(EncryptCredentialRequest request) =>
        Unwrap(Call<EncryptCredentialRequest, BridgeResponse<string>>(
            request,
            NativeMethods.rynat_encrypt_credential_json
        ));

    public string DecryptCredential(DecryptCredentialRequest request) =>
        Unwrap(Call<DecryptCredentialRequest, BridgeResponse<string>>(
            request,
            NativeMethods.rynat_decrypt_credential_json
        ));

    public QuickLink[] ListQuickLinks() =>
        Unwrap(Call<EmptyRequest, BridgeResponse<QuickLink[]>>(
            new EmptyRequest(),
            NativeMethods.rynat_list_quick_links_json
        ));

    public void DeleteQuickLink(DeleteQuickLinkRequest request) =>
        Unwrap(Call<DeleteQuickLinkRequest, BridgeResponse<bool>>(
            request,
            NativeMethods.rynat_delete_quick_link_json
        ));

    public SmbConnectResult SmbConnect(SmbConnectRequest request) =>
        Unwrap(Call<SmbConnectRequest, BridgeResponse<SmbConnectResult>>(
            request,
            NativeMethods.rynat_smb_connect_json
        ));

    public SmbConnectResult SmbConnectStoredCredential(SmbConnectStoredCredentialRequest request) =>
        Unwrap(Call<SmbConnectStoredCredentialRequest, BridgeResponse<SmbConnectResult>>(
            request,
            NativeMethods.rynat_smb_connect_stored_credential_json
        ));

    public SmbFileItem[] SmbListDirectory(SmbListDirectoryRequest request) =>
        Unwrap(Call<SmbListDirectoryRequest, BridgeResponse<SmbFileItem[]>>(
            request,
            NativeMethods.rynat_smb_list_directory_json
        ));

    public SmbCachedFile SmbCacheFile(SmbCacheFileRequest request) =>
        Unwrap(Call<SmbCacheFileRequest, BridgeResponse<SmbCachedFile>>(
            request,
            NativeMethods.rynat_smb_cache_file_json
        ));

    public SmbWriteResult SmbUploadFile(SmbUploadFileRequest request) =>
        Unwrap(Call<SmbUploadFileRequest, BridgeResponse<SmbWriteResult>>(
            request,
            NativeMethods.rynat_smb_upload_file_json
        ));

    public SmbWriteResult SmbCreateDirectory(SmbCreateDirectoryRequest request) =>
        Unwrap(Call<SmbCreateDirectoryRequest, BridgeResponse<SmbWriteResult>>(
            request,
            NativeMethods.rynat_smb_create_directory_json
        ));

    public SmbWriteResult SmbRename(SmbRenameRequest request) =>
        Unwrap(Call<SmbRenameRequest, BridgeResponse<SmbWriteResult>>(
            request,
            NativeMethods.rynat_smb_rename_json
        ));

    public SmbWriteResult SmbCopyFile(SmbCopyFileRequest request) =>
        Unwrap(Call<SmbCopyFileRequest, BridgeResponse<SmbWriteResult>>(
            request,
            NativeMethods.rynat_smb_copy_file_json
        ));

    public SmbWriteResult SmbDelete(SmbDeleteRequest request) =>
        Unwrap(Call<SmbDeleteRequest, BridgeResponse<SmbWriteResult>>(
            request,
            NativeMethods.rynat_smb_delete_json
        ));

    public void SmbDisconnect(SmbConnectionScopedRequest request) =>
        Unwrap(Call<SmbConnectionScopedRequest, BridgeResponse<bool>>(
            request,
            NativeMethods.rynat_smb_disconnect_json
        ));

    public SmbDiagnostics SmbDiagnostics(SmbConnectionScopedRequest request) =>
        Unwrap(Call<SmbConnectionScopedRequest, BridgeResponse<SmbDiagnostics>>(
            request,
            NativeMethods.rynat_smb_diagnostics_json
        ));

    public void SmbCancelOperation(SmbCancelOperationRequest request) =>
        Unwrap(Call<SmbCancelOperationRequest, BridgeResponse<bool>>(
            request,
            NativeMethods.rynat_smb_cancel_operation_json
        ));

    public SmbTaskStartResult SmbStartTask(SmbStartTaskRequest request) =>
        Unwrap(Call<SmbStartTaskRequest, BridgeResponse<SmbTaskStartResult>>(
            request,
            NativeMethods.rynat_smb_start_task_json
        ));

    public SmbTaskStatus SmbPollTask(SmbTaskRequest request) =>
        Unwrap(Call<SmbTaskRequest, BridgeResponse<SmbTaskStatus>>(
            request,
            NativeMethods.rynat_smb_poll_task_json
        ));

    public SmbTaskStatus SmbCancelTask(SmbTaskRequest request) =>
        Unwrap(Call<SmbTaskRequest, BridgeResponse<SmbTaskStatus>>(
            request,
            NativeMethods.rynat_smb_cancel_task_json
        ));

    public void SmbClearTask(SmbTaskRequest request) =>
        Unwrap(Call<SmbTaskRequest, BridgeResponse<bool>>(
            request,
            NativeMethods.rynat_smb_clear_task_json
        ));

    private static TResponse Call<TRequest, TResponse>(
        TRequest request,
        Func<IntPtr, IntPtr> nativeCall
    )
    {
        var json = JsonSerializer.Serialize(
            request,
            RynatJsonContext.Default.Options
        );
        var input = StringToUtf8(json);
        try
        {
            var output = nativeCall(input);
            if (output == IntPtr.Zero)
            {
                throw new RynatCoreBridgeException("rynat-core returned null", null);
            }

            try
            {
                var outputJson = PtrToUtf8(output);
                return JsonSerializer.Deserialize<TResponse>(
                    outputJson,
                    RynatJsonContext.Default.Options
                ) ?? throw new RynatCoreBridgeException("rynat-core returned empty JSON", null);
            }
            finally
            {
                NativeMethods.rynat_free_string(output);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input);
        }
    }

    private static T Unwrap<T>(BridgeResponse<T> response)
    {
        if (response.Ok && response.Data is not null)
        {
            return response.Data;
        }

        throw new RynatCoreBridgeException(
            response.Error ?? "rynat-core returned an unknown error",
            response.ErrorCode
        );
    }

    private static T? UnwrapNullable<T>(BridgeResponse<T?> response)
    {
        if (response.Ok)
        {
            return response.Data;
        }

        throw new RynatCoreBridgeException(
            response.Error ?? "rynat-core returned an unknown error",
            response.ErrorCode
        );
    }

    private static IntPtr StringToUtf8(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return pointer;
    }

    private static string PtrToUtf8(IntPtr pointer)
    {
        // PtrToStringUTF8 一次完成 UTF-8 解码，避免逐字节 ReadByte 扫描（大 JSON 响应 O(n²)）。
        return Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
    }
}

public sealed class RynatCoreBridgeException : InvalidOperationException
{
    public RynatCoreBridgeException(string message, string? errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }

    public string? ErrorCode { get; }
}

internal static partial class NativeMethods
{
    private const string CoreLibrary = "rynat_core";

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_generate_link_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_build_link_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_activate_link_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_preview_plan_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_upload_plan_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_redirect_page_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_open_store_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_app_bootstrap_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_save_server_profile_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_set_active_server_profile_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_delete_server_profile_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_save_server_credential_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_update_server_credential_options_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_delete_server_credential_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_encrypt_credential_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_decrypt_credential_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_list_quick_links_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_delete_quick_link_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_smb_connect_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_smb_connect_stored_credential_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_smb_list_directory_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_smb_cache_file_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_smb_upload_file_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_smb_create_directory_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_smb_rename_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_smb_copy_file_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_smb_delete_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_smb_disconnect_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_smb_diagnostics_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_smb_cancel_operation_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_smb_start_task_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_smb_poll_task_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_smb_cancel_task_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rynat_smb_clear_task_json(IntPtr inputJson);

    [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rynat_free_string(IntPtr value);
}

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

[JsonSerializable(typeof(BridgeResponse<bool>))]
[JsonSerializable(typeof(BridgeResponse<string>))]
[JsonSerializable(typeof(BridgeResponse<AppBootstrapState>))]
[JsonSerializable(typeof(BridgeResponse<StoredServerProfile>))]
[JsonSerializable(typeof(BridgeResponse<StoredServerCredential>))]
[JsonSerializable(typeof(BridgeResponse<QuickLink>))]
[JsonSerializable(typeof(BridgeResponse<QuickLink[]>))]
[JsonSerializable(typeof(BridgeResponse<LinkActivation>))]
[JsonSerializable(typeof(BridgeResponse<PreviewPlan>))]
[JsonSerializable(typeof(BridgeResponse<TransferPlan>))]
[JsonSerializable(typeof(BridgeResponse<SmbConnectResult>))]
[JsonSerializable(typeof(BridgeResponse<SmbFileItem[]>))]
[JsonSerializable(typeof(BridgeResponse<SmbCachedFile>))]
[JsonSerializable(typeof(BridgeResponse<SmbWriteResult>))]
[JsonSerializable(typeof(BridgeResponse<SmbDiagnostics>))]
[JsonSerializable(typeof(BridgeResponse<SmbTaskStartResult>))]
[JsonSerializable(typeof(BridgeResponse<SmbTaskStatus>))]
[JsonSerializable(typeof(QuickLinkTarget))]
[JsonSerializable(typeof(LocalFileEndpoint))]
[JsonSerializable(typeof(TransferEndpoint))]
[JsonSerializable(typeof(EmptyRequest))]
[JsonSerializable(typeof(GenerateLinkRequest))]
[JsonSerializable(typeof(BuildLinkRequest))]
[JsonSerializable(typeof(ActivateLinkRequest))]
[JsonSerializable(typeof(PreviewPlanRequest))]
[JsonSerializable(typeof(UploadPlanRequest))]
[JsonSerializable(typeof(RedirectPageRequest))]
[JsonSerializable(typeof(OpenStoreRequest))]
[JsonSerializable(typeof(SaveServerProfileRequest))]
[JsonSerializable(typeof(SetActiveServerProfileRequest))]
[JsonSerializable(typeof(DeleteServerProfileRequest))]
[JsonSerializable(typeof(SaveServerCredentialRequest))]
[JsonSerializable(typeof(UpdateServerCredentialOptionsRequest))]
[JsonSerializable(typeof(DeleteServerCredentialRequest))]
[JsonSerializable(typeof(EncryptCredentialRequest))]
[JsonSerializable(typeof(DecryptCredentialRequest))]
[JsonSerializable(typeof(DeleteQuickLinkRequest))]
[JsonSerializable(typeof(SmbConnectRequest))]
[JsonSerializable(typeof(SmbConnectStoredCredentialRequest))]
[JsonSerializable(typeof(SmbListDirectoryRequest))]
[JsonSerializable(typeof(SmbCacheFileRequest))]
[JsonSerializable(typeof(SmbUploadFileRequest))]
[JsonSerializable(typeof(SmbCreateDirectoryRequest))]
[JsonSerializable(typeof(SmbRenameRequest))]
[JsonSerializable(typeof(SmbCopyFileRequest))]
[JsonSerializable(typeof(SmbDeleteRequest))]
[JsonSerializable(typeof(SmbConnectionScopedRequest))]
[JsonSerializable(typeof(SmbCancelOperationRequest))]
[JsonSerializable(typeof(SmbStartTaskRequest))]
[JsonSerializable(typeof(SmbTaskRequest))]
internal sealed partial class RynatJsonContext : JsonSerializerContext
{
}
