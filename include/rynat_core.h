#ifndef RYNAT_CORE_H
#define RYNAT_CORE_H

#ifdef __cplusplus
extern "C" {
#endif

// All functions accept a UTF-8 JSON string and return a heap-allocated UTF-8 JSON string.
// The caller must release every non-null return value with rynat_free_string.
char *rynat_generate_link_json(const char *input_json);
char *rynat_build_link_json(const char *input_json);
char *rynat_activate_link_json(const char *input_json);
char *rynat_preview_plan_json(const char *input_json);
char *rynat_upload_plan_json(const char *input_json);
char *rynat_redirect_page_json(const char *input_json);
char *rynat_open_store_json(const char *input_json);
char *rynat_app_bootstrap_json(const char *input_json);
char *rynat_save_server_profile_json(const char *input_json);
char *rynat_set_active_server_profile_json(const char *input_json);
char *rynat_delete_server_profile_json(const char *input_json);
char *rynat_save_server_credential_json(const char *input_json);
char *rynat_update_server_credential_options_json(const char *input_json);
char *rynat_delete_server_credential_json(const char *input_json);
char *rynat_encrypt_credential_json(const char *input_json);
char *rynat_decrypt_credential_json(const char *input_json);
char *rynat_list_quick_links_json(const char *input_json);
char *rynat_delete_quick_link_json(const char *input_json);
char *rynat_smb_connect_json(const char *input_json);
char *rynat_smb_connect_stored_credential_json(const char *input_json);
char *rynat_smb_list_directory_json(const char *input_json);
char *rynat_smb_cache_file_json(const char *input_json);
char *rynat_smb_upload_file_json(const char *input_json);
char *rynat_smb_create_directory_json(const char *input_json);
char *rynat_smb_rename_json(const char *input_json);
char *rynat_smb_copy_file_json(const char *input_json);
char *rynat_smb_delete_json(const char *input_json);
char *rynat_smb_disconnect_json(const char *input_json);
char *rynat_smb_diagnostics_json(const char *input_json);
char *rynat_smb_cancel_operation_json(const char *input_json);
char *rynat_smb_start_task_json(const char *input_json);
char *rynat_smb_poll_task_json(const char *input_json);
char *rynat_smb_cancel_task_json(const char *input_json);
char *rynat_smb_clear_task_json(const char *input_json);
void rynat_free_string(char *value);

#ifdef __cplusplus
}
#endif

#endif
