using System;
using System.Runtime.InteropServices;

namespace Rynat.Client;

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
