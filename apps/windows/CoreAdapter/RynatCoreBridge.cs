using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

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
        return Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
    }
}
