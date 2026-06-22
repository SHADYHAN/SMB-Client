use std::ffi::{CStr, CString, c_char};
use std::ptr;
use std::thread;
use std::time::{SystemTime, UNIX_EPOCH};

use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::sync::{OnceLock, RwLock};
use uuid::Uuid;

use crate::credential::{decrypt_credential, encrypt_credential};
use crate::error::{CoreError, CoreResult, SmbErrorCode};
use crate::link::{LinkKind, QuickLink, QuickLinkTarget};
use crate::preview::PreviewRequest;
use crate::server::{AuthMode, ServerCredential, ServerProfile, SmbDialectPreference};
use crate::smb_client::{
    SmbCacheFileRequest, SmbCancelOperationRequest, SmbConnectRequest, SmbCopyFileRequest,
    SmbCreateDirectoryRequest, SmbDeleteRequest, SmbListDirectoryRequest, SmbRenameRequest,
    SmbUploadFileRequest,
};
use crate::transfer::TransferPlan;

static SMB_CONNECTIONS: OnceLock<RwLock<HashMap<String, crate::smb_client::SmbConnection>>> =
    OnceLock::new();
static SMB_RUNTIME: OnceLock<Result<tokio::runtime::Runtime, String>> = OnceLock::new();
static SMB_TASKS: OnceLock<RwLock<HashMap<String, SmbTaskRecord>>> = OnceLock::new();
static CORE_STORE: OnceLock<RwLock<Option<crate::storage::CoreStore>>> = OnceLock::new();
const COMPLETED_TASK_TTL_MS: u64 = 10 * 60 * 1000;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct GenerateLinkRequest {
    pub server_host: String,
    pub share: String,
    pub path: String,
    pub kind: LinkKind,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct BuildLinkRequest {
    pub server_host: String,
    pub share: String,
    pub path: String,
    pub kind: LinkKind,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ActivateLinkRequest {
    pub raw_link: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct PreviewPlanRequest {
    pub server_host: String,
    pub share: String,
    pub path: String,
    pub kind: LinkKind,
    pub max_edge_px: Option<u32>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct UploadPlanRequest {
    pub local_path: String,
    pub server_host: String,
    pub share: String,
    pub remote_path: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct RedirectPageRequest {
    pub target_url: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct OpenStoreRequest {
    pub path: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SaveServerProfileRequest {
    pub id: Option<String>,
    pub display_name: String,
    pub host: String,
    pub username: Option<String>,
    pub auth_mode: Option<AuthMode>,
    pub dialect_preference: Option<SmbDialectPreference>,
    pub set_active: Option<bool>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SetActiveServerProfileRequest {
    pub id: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DeleteServerProfileRequest {
    pub id: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SaveServerCredentialRequest {
    pub server_profile_id: String,
    pub username: String,
    pub password: String,
    pub remember_password: bool,
    pub auto_login: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct UpdateServerCredentialOptionsRequest {
    pub server_profile_id: String,
    pub remember_password: bool,
    pub auto_login: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ServerCredentialSummary {
    pub server_profile_id: String,
    pub username: String,
    pub remember_password: bool,
    pub auto_login: bool,
    pub updated_at: String,
}

impl From<ServerCredential> for ServerCredentialSummary {
    fn from(credential: ServerCredential) -> Self {
        Self {
            server_profile_id: credential.server_profile_id,
            username: credential.username,
            remember_password: credential.remember_password,
            auto_login: credential.auto_login,
            updated_at: credential.updated_at,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DeleteServerCredentialRequest {
    pub server_profile_id: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct EncryptCredentialRequest {
    pub password: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DecryptCredentialRequest {
    pub encrypted: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DeleteQuickLinkRequest {
    pub id: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SmbConnectionScopedRequest {
    #[serde(default)]
    pub connection_id: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SmbConnectStoredCredentialRequest {
    pub server_profile_id: String,
    #[serde(default)]
    pub connection_id: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum SmbTaskOperation {
    CacheFile,
    UploadFile,
    CopyFile,
    Delete,
    CreateDirectory,
    Rename,
    ListDirectory,
    #[cfg(test)]
    TestNoop,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SmbStartTaskRequest {
    pub operation: SmbTaskOperation,
    pub payload: serde_json::Value,
    #[serde(default)]
    pub operation_id: Option<String>,
    #[serde(default)]
    pub server_profile_id: Option<String>,
    #[serde(default)]
    pub use_isolated_connection: Option<bool>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SmbTaskRequest {
    pub task_id: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SmbTaskStartResult {
    pub task_id: String,
    pub operation_id: String,
    pub state: SmbTaskState,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SmbTaskStatus {
    pub task_id: String,
    pub operation_id: String,
    pub operation: SmbTaskOperation,
    pub state: SmbTaskState,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub connection_id: Option<String>,
    pub started_at_ms: u64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub finished_at_ms: Option<u64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub data: Option<serde_json::Value>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub error: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub error_code: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum SmbTaskState {
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

#[derive(Debug, Clone)]
struct SmbTaskRecord {
    task_id: String,
    operation_id: String,
    operation: SmbTaskOperation,
    state: SmbTaskState,
    connection_id: Option<String>,
    started_at_ms: u64,
    finished_at_ms: Option<u64>,
    data: Option<serde_json::Value>,
    error: Option<String>,
    error_code: Option<String>,
}

#[derive(Debug, Clone)]
struct TaskConnectionSpec {
    server_profile_id: String,
    connection_id: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AppBootstrapState {
    pub server_profiles: Vec<ServerProfile>,
    pub active_server: Option<ServerProfile>,
    pub active_credential: Option<ServerCredentialSummary>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct BridgeResponse<T> {
    pub ok: bool,
    pub data: Option<T>,
    pub error: Option<String>,
    pub error_code: Option<String>,
}

impl<T> BridgeResponse<T> {
    fn ok(data: T) -> Self {
        Self {
            ok: true,
            data: Some(data),
            error: None,
            error_code: None,
        }
    }

    fn error(error: &CoreError) -> Self {
        Self {
            ok: false,
            data: None,
            error: Some(error.to_string()),
            error_code: Some(error_code(error).to_string()),
        }
    }
}

pub fn generate_link_json(input: &str) -> CoreResult<String> {
    let request: GenerateLinkRequest = serde_json::from_str(input)?;
    let target = QuickLinkTarget::new(
        request.server_host,
        request.share,
        request.path,
        None,
        request.kind,
    );
    let link = QuickLink::create(target)?;
    let _ = app_store().and_then(|store| store.save_quick_link(&link));
    serde_json::to_string(&BridgeResponse::ok(link)).map_err(CoreError::from)
}

pub fn build_link_json(input: &str) -> CoreResult<String> {
    let request: BuildLinkRequest = serde_json::from_str(input)?;
    let target = QuickLinkTarget::new(
        request.server_host,
        request.share,
        request.path,
        None,
        request.kind,
    );
    let link = QuickLink::create(target)?;
    serde_json::to_string(&BridgeResponse::ok(link)).map_err(CoreError::from)
}

pub fn activate_link_json(input: &str) -> CoreResult<String> {
    let request: ActivateLinkRequest = serde_json::from_str(input)?;
    let activation = crate::CoreSession::new(app_store()?).activate_link(&request.raw_link)?;
    serde_json::to_string(&BridgeResponse::ok(activation)).map_err(CoreError::from)
}

pub fn preview_plan_json(input: &str) -> CoreResult<String> {
    let request: PreviewPlanRequest = serde_json::from_str(input)?;
    let target = QuickLinkTarget::new(
        request.server_host,
        request.share,
        request.path,
        None,
        request.kind,
    );
    let plan = PreviewRequest::new(
        target,
        request
            .max_edge_px
            .unwrap_or(crate::DEFAULT_PREVIEW_MAX_EDGE_PX),
    )
    .plan();
    serde_json::to_string(&BridgeResponse::ok(plan)).map_err(CoreError::from)
}

pub fn upload_plan_json(input: &str) -> CoreResult<String> {
    let request: UploadPlanRequest = serde_json::from_str(input)?;
    let plan = TransferPlan::upload(
        request.local_path,
        request.server_host,
        request.share,
        request.remote_path,
    )?;
    serde_json::to_string(&BridgeResponse::ok(plan)).map_err(CoreError::from)
}

pub fn redirect_page_json(input: &str) -> CoreResult<String> {
    let request: RedirectPageRequest = serde_json::from_str(input)?;
    let html = crate::redirect_page::build_invisible_redirect_page_for_url(
        &request.target_url,
        &crate::redirect_page::RedirectPageOptions::default(),
    )?;
    serde_json::to_string(&BridgeResponse::ok(html)).map_err(CoreError::from)
}

pub fn open_store_json(input: &str) -> CoreResult<String> {
    let request: OpenStoreRequest = serde_json::from_str(input)?;
    if request.path.trim().is_empty() {
        return Err(CoreError::MissingField("path"));
    }
    let store = crate::storage::CoreStore::open(request.path)?;
    store.ensure_default_server_profile()?;
    set_app_store(store.clone())?;
    let state = bootstrap_state(&store)?;
    serde_json::to_string(&BridgeResponse::ok(state)).map_err(CoreError::from)
}

pub fn app_bootstrap_json(_input: &str) -> CoreResult<String> {
    let store = app_store()?;
    store.ensure_default_server_profile()?;
    let state = bootstrap_state(&store)?;
    serde_json::to_string(&BridgeResponse::ok(state)).map_err(CoreError::from)
}

pub fn save_server_profile_json(input: &str) -> CoreResult<String> {
    let request: SaveServerProfileRequest = serde_json::from_str(input)?;
    let store = app_store()?;
    let profile = store.save_server_profile_from_parts(
        request.id.as_deref(),
        &request.display_name,
        &request.host,
        request.username,
        request.auth_mode.unwrap_or(AuthMode::UsernamePassword),
        request
            .dialect_preference
            .unwrap_or(SmbDialectPreference::Smb3Preferred),
        request.set_active.unwrap_or(false),
    )?;
    serde_json::to_string(&BridgeResponse::ok(profile)).map_err(CoreError::from)
}

pub fn set_active_server_profile_json(input: &str) -> CoreResult<String> {
    let request: SetActiveServerProfileRequest = serde_json::from_str(input)?;
    let store = app_store()?;
    if store.find_server_profile_by_id(&request.id)?.is_none() {
        return Err(CoreError::InvalidLink(
            "server profile not found".to_string(),
        ));
    }
    store.set_active_server_profile(&request.id)?;
    let state = bootstrap_state(&store)?;
    serde_json::to_string(&BridgeResponse::ok(state)).map_err(CoreError::from)
}

pub fn delete_server_profile_json(input: &str) -> CoreResult<String> {
    let request: DeleteServerProfileRequest = serde_json::from_str(input)?;
    let store = app_store()?;
    store.delete_server_profile(&request.id)?;
    let state = bootstrap_state(&store)?;
    serde_json::to_string(&BridgeResponse::ok(state)).map_err(CoreError::from)
}

pub fn save_server_credential_json(input: &str) -> CoreResult<String> {
    let request: SaveServerCredentialRequest = serde_json::from_str(input)?;
    if !request.remember_password {
        app_store()?.delete_server_credential(&request.server_profile_id)?;
        let summary = ServerCredentialSummary {
            server_profile_id: request.server_profile_id,
            username: request.username,
            remember_password: false,
            auto_login: false,
            updated_at: String::new(),
        };
        return serde_json::to_string(&BridgeResponse::ok(summary)).map_err(CoreError::from);
    }
    let credential = ServerCredential::new(
        request.server_profile_id,
        request.username,
        request.password,
        request.remember_password,
        request.auto_login,
    )?;
    let store = app_store()?;
    store.save_server_credential(&credential)?;
    serde_json::to_string(&BridgeResponse::ok(ServerCredentialSummary::from(
        credential,
    )))
    .map_err(CoreError::from)
}

pub fn update_server_credential_options_json(input: &str) -> CoreResult<String> {
    let request: UpdateServerCredentialOptionsRequest = serde_json::from_str(input)?;
    if !request.remember_password {
        app_store()?.delete_server_credential(&request.server_profile_id)?;
        return serde_json::to_string(&BridgeResponse::ok(Option::<ServerCredentialSummary>::None))
            .map_err(CoreError::from);
    }
    let credential = app_store()?
        .update_server_credential_options(
            &request.server_profile_id,
            request.remember_password,
            request.auto_login,
        )?
        .ok_or_else(|| CoreError::Crypto("未找到已保存的登录信息，请重新登录".to_string()))?;
    serde_json::to_string(&BridgeResponse::ok(Some(ServerCredentialSummary::from(
        credential,
    ))))
    .map_err(CoreError::from)
}

pub fn delete_server_credential_json(input: &str) -> CoreResult<String> {
    let request: DeleteServerCredentialRequest = serde_json::from_str(input)?;
    app_store()?.delete_server_credential(&request.server_profile_id)?;
    serde_json::to_string(&BridgeResponse::ok(true)).map_err(CoreError::from)
}

pub fn encrypt_credential_json(input: &str) -> CoreResult<String> {
    let request: EncryptCredentialRequest = serde_json::from_str(input)?;
    let encrypted = encrypt_credential(&request.password)?;
    serde_json::to_string(&BridgeResponse::ok(encrypted)).map_err(CoreError::from)
}

pub fn decrypt_credential_json(input: &str) -> CoreResult<String> {
    let request: DecryptCredentialRequest = serde_json::from_str(input)?;
    let password = decrypt_credential(&request.encrypted)?;
    serde_json::to_string(&BridgeResponse::ok(password)).map_err(CoreError::from)
}

pub fn list_quick_links_json(_input: &str) -> CoreResult<String> {
    let links = app_store()?.list_quick_links()?;
    serde_json::to_string(&BridgeResponse::ok(links)).map_err(CoreError::from)
}

pub fn delete_quick_link_json(input: &str) -> CoreResult<String> {
    let request: DeleteQuickLinkRequest = serde_json::from_str(input)?;
    app_store()?.delete_quick_link(&request.id)?;
    serde_json::to_string(&BridgeResponse::ok(true)).map_err(CoreError::from)
}

pub fn smb_connect_json(input: &str) -> CoreResult<String> {
    let request: SmbConnectRequest = serde_json::from_str(input)?;
    let connection_id =
        crate::smb_client::normalize_connection_id(request.connection_id.as_deref());
    let result = block_on_smb(async {
        smb_connection_for(Some(&connection_id))?
            .connect_and_discover(
                &connection_id,
                &request.host,
                &request.username,
                &request.password,
            )
            .await
    })?;
    serde_json::to_string(&BridgeResponse::ok(result)).map_err(CoreError::from)
}

pub fn smb_connect_stored_credential_json(input: &str) -> CoreResult<String> {
    let request: SmbConnectStoredCredentialRequest = serde_json::from_str(input)?;
    let server_profile_id = request.server_profile_id.trim();
    if server_profile_id.is_empty() {
        return Err(CoreError::MissingField("server_profile_id"));
    }

    let store = app_store()?;
    let profile = store
        .find_server_profile_by_id(server_profile_id)?
        .ok_or_else(|| CoreError::InvalidLink("server profile not found".to_string()))?;
    let credential = store
        .server_credential(&profile.id)?
        .ok_or_else(|| CoreError::Crypto("未找到已保存的登录信息，请重新登录".to_string()))?;
    let requested_connection_id = request
        .connection_id
        .as_deref()
        .filter(|value| !value.trim().is_empty())
        .unwrap_or(&profile.id);
    let connection_id = crate::smb_client::normalize_connection_id(Some(requested_connection_id));
    let host = profile.link_host();
    let result = block_on_smb(async {
        smb_connection_for(Some(&connection_id))?
            .connect_and_discover(
                &connection_id,
                &host,
                &credential.username,
                &credential.password,
            )
            .await
    })?;
    serde_json::to_string(&BridgeResponse::ok(result)).map_err(CoreError::from)
}

pub fn smb_list_directory_json(input: &str) -> CoreResult<String> {
    let request: SmbListDirectoryRequest = serde_json::from_str(input)?;
    let operation_id = request.operation_id.clone();
    let result = block_on_smb(async {
        smb_connection_for(request.connection_id.as_deref())?
            .list_directory(
                &request.share,
                &request.path,
                request.operation_id.as_deref(),
            )
            .await
    });
    let result = clear_operation_result(operation_id.as_deref(), result)?;
    serde_json::to_string(&BridgeResponse::ok(result)).map_err(CoreError::from)
}

pub fn smb_cache_file_json(input: &str) -> CoreResult<String> {
    let request: SmbCacheFileRequest = serde_json::from_str(input)?;
    let operation_id = request.operation_id.clone();
    let result = clear_operation_after(operation_id.as_deref(), || {
        block_on_smb(async {
            smb_connection_for(request.connection_id.as_deref())?
                .cache_file(
                    &request.share,
                    &request.path,
                    &request.local_path,
                    request.max_bytes,
                    request.operation_id.as_deref(),
                )
                .await
        })
    })?;
    serde_json::to_string(&BridgeResponse::ok(result)).map_err(CoreError::from)
}

pub fn smb_upload_file_json(input: &str) -> CoreResult<String> {
    let request: SmbUploadFileRequest = serde_json::from_str(input)?;
    let operation_id = request.operation_id.clone();
    let result = clear_operation_after(operation_id.as_deref(), || {
        block_on_smb(async {
            smb_connection_for(request.connection_id.as_deref())?
                .upload_file(
                    &request.share,
                    &request.local_path,
                    &request.remote_path,
                    request.replace_existing,
                    request.operation_id.as_deref(),
                )
                .await
        })
    })?;
    serde_json::to_string(&BridgeResponse::ok(result)).map_err(CoreError::from)
}

pub fn smb_create_directory_json(input: &str) -> CoreResult<String> {
    let request: SmbCreateDirectoryRequest = serde_json::from_str(input)?;
    let operation_id = request.operation_id.clone();
    let result = block_on_smb(async {
        smb_connection_for(request.connection_id.as_deref())?
            .create_directory(
                &request.share,
                &request.path,
                request.operation_id.as_deref(),
            )
            .await
    });
    let result = clear_operation_result(operation_id.as_deref(), result)?;
    serde_json::to_string(&BridgeResponse::ok(result)).map_err(CoreError::from)
}

pub fn smb_rename_json(input: &str) -> CoreResult<String> {
    let request: SmbRenameRequest = serde_json::from_str(input)?;
    let operation_id = request.operation_id.clone();
    let result = block_on_smb(async {
        smb_connection_for(request.connection_id.as_deref())?
            .rename_path(
                &request.share,
                &request.from_path,
                &request.to_path,
                request.operation_id.as_deref(),
            )
            .await
    });
    let result = clear_operation_result(operation_id.as_deref(), result)?;
    serde_json::to_string(&BridgeResponse::ok(result)).map_err(CoreError::from)
}

pub fn smb_copy_file_json(input: &str) -> CoreResult<String> {
    let request: SmbCopyFileRequest = serde_json::from_str(input)?;
    let operation_id = request.operation_id.clone();
    let result = clear_operation_after(operation_id.as_deref(), || {
        block_on_smb(async {
            smb_connection_for(request.connection_id.as_deref())?
                .copy_file(
                    &request.source_share,
                    &request.source_path,
                    &request.target_share,
                    &request.target_path,
                    request.replace_existing,
                    request.operation_id.as_deref(),
                )
                .await
        })
    })?;
    serde_json::to_string(&BridgeResponse::ok(result)).map_err(CoreError::from)
}

pub fn smb_delete_json(input: &str) -> CoreResult<String> {
    let request: SmbDeleteRequest = serde_json::from_str(input)?;
    let operation_id = request.operation_id.clone();
    let result = block_on_smb(async {
        smb_connection_for(request.connection_id.as_deref())?
            .delete_path(
                &request.share,
                &request.path,
                request.is_dir,
                request.operation_id.as_deref(),
            )
            .await
    });
    let result = clear_operation_result(operation_id.as_deref(), result)?;
    serde_json::to_string(&BridgeResponse::ok(result)).map_err(CoreError::from)
}

pub fn smb_disconnect_json(input: &str) -> CoreResult<String> {
    let request = scoped_smb_request(input)?;
    let connection_id =
        crate::smb_client::normalize_connection_id(request.connection_id.as_deref());
    disconnect_smb_connection(&connection_id);
    serde_json::to_string(&BridgeResponse::ok(true)).map_err(CoreError::from)
}

pub fn smb_diagnostics_json(input: &str) -> CoreResult<String> {
    let request = scoped_smb_request(input)?;
    let result = block_on_smb(async {
        Ok::<_, String>(
            smb_connection_for(request.connection_id.as_deref())?
                .diagnostics(Some(&crate::smb_client::normalize_connection_id(
                    request.connection_id.as_deref(),
                )))
                .await,
        )
    })?;
    serde_json::to_string(&BridgeResponse::ok(result)).map_err(CoreError::from)
}

pub fn smb_cancel_operation_json(input: &str) -> CoreResult<String> {
    let request: SmbCancelOperationRequest = serde_json::from_str(input)?;
    crate::smb_client::cancel_operation(&request.operation_id);
    serde_json::to_string(&BridgeResponse::ok(true)).map_err(CoreError::from)
}

pub fn smb_start_task_json(input: &str) -> CoreResult<String> {
    let request: SmbStartTaskRequest = serde_json::from_str(input)?;
    prune_finished_task_records(now_ms());
    let task_id = Uuid::new_v4().to_string();
    let operation_id = request
        .operation_id
        .filter(|value| !value.trim().is_empty())
        .unwrap_or_else(|| Uuid::new_v4().to_string());
    let operation = request.operation;
    let payload = request.payload;
    let use_isolated_connection = request.use_isolated_connection.unwrap_or(true);
    let task_connection = if use_isolated_connection {
        let server_profile_id = request
            .server_profile_id
            .filter(|value| !value.trim().is_empty())
            .ok_or(CoreError::MissingField("server_profile_id"))?;
        Some(TaskConnectionSpec {
            server_profile_id: server_profile_id.clone(),
            connection_id: format!("task:{}:{}", server_profile_id.trim(), task_id),
        })
    } else {
        None
    };
    let task_connection_id = task_connection
        .as_ref()
        .map(|connection| connection.connection_id.clone());

    insert_task_record(SmbTaskRecord {
        task_id: task_id.clone(),
        operation_id: operation_id.clone(),
        operation: operation.clone(),
        state: SmbTaskState::Queued,
        connection_id: task_connection_id.clone(),
        started_at_ms: now_ms(),
        finished_at_ms: None,
        data: None,
        error: None,
        error_code: None,
    })?;

    let thread_task_id = task_id.clone();
    let thread_operation_id = operation_id.clone();
    let thread_task_connection = task_connection.clone();
    thread::spawn(move || {
        let _ = update_task_record(&thread_task_id, |record| {
            if matches!(record.state, SmbTaskState::Queued) {
                record.state = SmbTaskState::Running;
            }
        });
        let (result, connection_id) = match thread_task_connection {
            Some(connection) => match connect_task_connection(
                &connection.server_profile_id,
                &connection.connection_id,
            ) {
                Ok(()) => {
                    let result = run_smb_task(
                        operation,
                        payload,
                        &thread_operation_id,
                        Some(connection.connection_id.clone()),
                    );
                    (result, Some(connection.connection_id))
                }
                Err(error) => (Err(error), None),
            },
            None => (
                run_smb_task(operation, payload, &thread_operation_id, None),
                None,
            ),
        };
        let _ = update_task_record(&thread_task_id, |record| {
            record.finished_at_ms = Some(now_ms());
            record.connection_id = connection_id.clone();
            if matches!(record.state, SmbTaskState::Cancelled) {
                crate::smb_client::clear_operation(Some(&record.operation_id));
                return;
            }
            match result {
                Ok(data) => {
                    if crate::smb_client::is_operation_cancelled(Some(&record.operation_id)) {
                        record.state = SmbTaskState::Cancelled;
                        record.error = Some("操作已取消".to_string());
                        record.error_code = Some("cancelled".to_string());
                    } else {
                        record.state = SmbTaskState::Succeeded;
                        record.data = Some(data);
                    }
                }
                Err(error) => {
                    let code = error_code(&error).to_string();
                    record.state = if code == "cancelled"
                        || crate::smb_client::is_operation_cancelled(Some(&record.operation_id))
                    {
                        SmbTaskState::Cancelled
                    } else {
                        SmbTaskState::Failed
                    };
                    record.error = Some(error.to_string());
                    record.error_code = Some(code);
                }
            }
            crate::smb_client::clear_operation(Some(&record.operation_id));
        });
        crate::smb_client::clear_operation(Some(&thread_operation_id));
        if let Some(connection_id) = connection_id.as_deref() {
            disconnect_smb_connection(connection_id);
        }
    });

    let result = SmbTaskStartResult {
        task_id,
        operation_id,
        state: SmbTaskState::Queued,
    };
    serde_json::to_string(&BridgeResponse::ok(result)).map_err(CoreError::from)
}

pub fn smb_poll_task_json(input: &str) -> CoreResult<String> {
    let request: SmbTaskRequest = serde_json::from_str(input)?;
    let status = task_status(&request.task_id)?;
    serde_json::to_string(&BridgeResponse::ok(status)).map_err(CoreError::from)
}

pub fn smb_cancel_task_json(input: &str) -> CoreResult<String> {
    let request: SmbTaskRequest = serde_json::from_str(input)?;
    let status = task_status(&request.task_id)?;
    crate::smb_client::cancel_operation(&status.operation_id);
    if let Some(connection_id) = status.connection_id.as_deref() {
        disconnect_smb_connection(connection_id);
    }
    let _ = update_task_record(&request.task_id, |record| {
        if matches!(record.state, SmbTaskState::Queued | SmbTaskState::Running) {
            record.state = SmbTaskState::Cancelled;
            record.error = Some("操作已取消".to_string());
            record.error_code = Some("cancelled".to_string());
        }
    });
    let status = task_status(&request.task_id)?;
    serde_json::to_string(&BridgeResponse::ok(status)).map_err(CoreError::from)
}

pub fn smb_clear_task_json(input: &str) -> CoreResult<String> {
    let request: SmbTaskRequest = serde_json::from_str(input)?;
    let mut tasks = smb_tasks_cell()
        .write()
        .map_err(|error| CoreError::Storage(error.to_string()))?;
    let Some(record) = tasks.get(&request.task_id) else {
        return Err(CoreError::InvalidLink("task not found".to_string()));
    };
    if matches!(record.state, SmbTaskState::Queued | SmbTaskState::Running) {
        return Err(CoreError::InvalidLink(
            "task is still running; cancel it before clearing".to_string(),
        ));
    }
    crate::smb_client::clear_operation(Some(&record.operation_id));
    tasks.remove(&request.task_id);
    serde_json::to_string(&BridgeResponse::ok(true)).map_err(CoreError::from)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_generate_link_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, generate_link_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_build_link_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, build_link_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_activate_link_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, activate_link_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_preview_plan_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, preview_plan_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_upload_plan_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, upload_plan_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_redirect_page_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, redirect_page_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_open_store_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, open_store_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_app_bootstrap_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, app_bootstrap_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_save_server_profile_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, save_server_profile_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_set_active_server_profile_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, set_active_server_profile_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_delete_server_profile_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, delete_server_profile_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_save_server_credential_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, save_server_credential_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_update_server_credential_options_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, update_server_credential_options_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_delete_server_credential_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, delete_server_credential_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_encrypt_credential_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, encrypt_credential_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_decrypt_credential_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, decrypt_credential_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_list_quick_links_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, list_quick_links_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_delete_quick_link_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, delete_quick_link_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_smb_connect_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, smb_connect_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_smb_connect_stored_credential_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, smb_connect_stored_credential_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_smb_list_directory_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, smb_list_directory_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_smb_cache_file_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, smb_cache_file_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_smb_upload_file_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, smb_upload_file_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_smb_create_directory_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, smb_create_directory_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_smb_rename_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, smb_rename_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_smb_copy_file_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, smb_copy_file_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_smb_delete_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, smb_delete_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_smb_disconnect_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, smb_disconnect_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_smb_diagnostics_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, smb_diagnostics_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_smb_cancel_operation_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, smb_cancel_operation_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_smb_start_task_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, smb_start_task_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_smb_poll_task_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, smb_poll_task_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_smb_cancel_task_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, smb_cancel_task_json)
}

#[unsafe(no_mangle)]
pub extern "C" fn rynat_smb_clear_task_json(input: *const c_char) -> *mut c_char {
    bridge_result(input, smb_clear_task_json)
}

#[unsafe(no_mangle)]
/// # Safety
///
/// `value` must be a non-null pointer previously returned by a RYNAT Core FFI
/// function that transferred ownership of a `CString` with `into_raw`.
/// Passing any other pointer, or freeing the same pointer more than once, is
/// undefined behavior.
pub unsafe extern "C" fn rynat_free_string(value: *mut c_char) {
    if value.is_null() {
        return;
    }
    unsafe {
        let _ = CString::from_raw(value);
    }
}

fn bridge_result<F>(input: *const c_char, operation: F) -> *mut c_char
where
    F: FnOnce(&str) -> CoreResult<String>,
{
    let response = read_input(input)
        .and_then(|value| operation(&value))
        .unwrap_or_else(|error| {
            serde_json::to_string(&BridgeResponse::<serde_json::Value>::error(&error))
                .unwrap_or_else(|_| {
                    "{\"ok\":false,\"error\":\"unknown bridge error\",\"data\":null}".to_string()
                })
        });
    string_to_c(response)
}

fn read_input(input: *const c_char) -> CoreResult<String> {
    if input.is_null() {
        return Err(CoreError::InvalidLink("bridge input is null".to_string()));
    }
    let value = unsafe { CStr::from_ptr(input) }
        .to_str()
        .map_err(|error| CoreError::InvalidLink(error.to_string()))?;
    Ok(value.to_string())
}

fn string_to_c(value: String) -> *mut c_char {
    let sanitized = value.replace('\0', "");
    CString::new(sanitized)
        .map(CString::into_raw)
        .unwrap_or(ptr::null_mut())
}

fn scoped_smb_request(input: &str) -> CoreResult<SmbConnectionScopedRequest> {
    if input.trim().is_empty() {
        return Ok(SmbConnectionScopedRequest {
            connection_id: None,
        });
    }
    serde_json::from_str(input).map_err(CoreError::from)
}

fn smb_connections_cell() -> &'static RwLock<HashMap<String, crate::smb_client::SmbConnection>> {
    SMB_CONNECTIONS.get_or_init(|| RwLock::new(HashMap::new()))
}

fn smb_tasks_cell() -> &'static RwLock<HashMap<String, SmbTaskRecord>> {
    SMB_TASKS.get_or_init(|| RwLock::new(HashMap::new()))
}

fn insert_task_record(record: SmbTaskRecord) -> CoreResult<()> {
    smb_tasks_cell()
        .write()
        .map_err(|error| CoreError::Storage(error.to_string()))?
        .insert(record.task_id.clone(), record);
    Ok(())
}

fn prune_finished_task_records(now_ms: u64) {
    if let Ok(mut tasks) = smb_tasks_cell().write() {
        tasks.retain(|_, record| {
            record
                .finished_at_ms
                .is_none_or(|finished| now_ms.saturating_sub(finished) < COMPLETED_TASK_TTL_MS)
        });
    }
}

fn update_task_record(task_id: &str, operation: impl FnOnce(&mut SmbTaskRecord)) -> CoreResult<()> {
    let mut tasks = smb_tasks_cell()
        .write()
        .map_err(|error| CoreError::Storage(error.to_string()))?;
    let record = tasks
        .get_mut(task_id)
        .ok_or_else(|| CoreError::InvalidLink("task not found".to_string()))?;
    operation(record);
    Ok(())
}

fn task_status(task_id: &str) -> CoreResult<SmbTaskStatus> {
    let record = smb_tasks_cell()
        .read()
        .map_err(|error| CoreError::Storage(error.to_string()))?
        .get(task_id)
        .cloned()
        .ok_or_else(|| CoreError::InvalidLink("task not found".to_string()))?;
    Ok(SmbTaskStatus {
        task_id: record.task_id,
        operation_id: record.operation_id,
        operation: record.operation,
        state: record.state,
        connection_id: record.connection_id,
        started_at_ms: record.started_at_ms,
        finished_at_ms: record.finished_at_ms,
        data: record.data,
        error: record.error,
        error_code: record.error_code,
    })
}

fn run_smb_task(
    operation: SmbTaskOperation,
    payload: serde_json::Value,
    operation_id: &str,
    task_connection_id: Option<String>,
) -> CoreResult<serde_json::Value> {
    let mut payload = match payload {
        serde_json::Value::Object(map) => map,
        _ => {
            return Err(CoreError::InvalidLink(
                "task payload must be an object".to_string(),
            ));
        }
    };
    payload.insert(
        "operation_id".to_string(),
        serde_json::Value::String(operation_id.to_string()),
    );
    if let Some(connection_id) = task_connection_id.as_deref() {
        payload.insert(
            "connection_id".to_string(),
            serde_json::Value::String(connection_id.to_string()),
        );
    }
    let payload_text = serde_json::Value::Object(payload).to_string();

    let json = match operation {
        SmbTaskOperation::CacheFile => smb_cache_file_json(&payload_text)?,
        SmbTaskOperation::UploadFile => smb_upload_file_json(&payload_text)?,
        SmbTaskOperation::CopyFile => smb_copy_file_json(&payload_text)?,
        SmbTaskOperation::Delete => smb_delete_json(&payload_text)?,
        SmbTaskOperation::CreateDirectory => smb_create_directory_json(&payload_text)?,
        SmbTaskOperation::Rename => smb_rename_json(&payload_text)?,
        SmbTaskOperation::ListDirectory => smb_list_directory_json(&payload_text)?,
        #[cfg(test)]
        SmbTaskOperation::TestNoop => {
            if payload_text.contains("slow") {
                std::thread::sleep(std::time::Duration::from_millis(60));
            }
            serde_json::to_string(&BridgeResponse::ok(serde_json::json!({
                "operation_id": operation_id
            })))
            .map_err(CoreError::from)?
        }
    };
    let response: BridgeResponse<serde_json::Value> = serde_json::from_str(&json)?;
    if response.ok {
        Ok(response.data.unwrap_or(serde_json::Value::Null))
    } else {
        Err(CoreError::smb(
            response
                .error
                .unwrap_or_else(|| "SMB task failed".to_string()),
        ))
    }
}

fn now_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|value| value.as_millis().try_into().unwrap_or(u64::MAX))
        .unwrap_or(0)
}

fn smb_connection_for(
    connection_id: Option<&str>,
) -> Result<crate::smb_client::SmbConnection, String> {
    let normalized = crate::smb_client::normalize_connection_id(connection_id);
    if let Some(connection) = smb_connections_cell()
        .read()
        .map_err(|error| format!("SMB 连接状态不可用: {}", error))?
        .get(&normalized)
        .cloned()
    {
        return Ok(connection);
    }

    let mut connections = smb_connections_cell()
        .write()
        .map_err(|error| format!("SMB 连接状态不可用: {}", error))?;
    Ok(connections
        .entry(normalized)
        .or_insert_with(crate::smb_client::SmbConnection::new)
        .clone())
}

fn connect_task_connection(server_profile_id: &str, connection_id: &str) -> CoreResult<()> {
    let server_profile_id = server_profile_id.trim();
    if server_profile_id.is_empty() {
        return Err(CoreError::MissingField("server_profile_id"));
    }
    let store = app_store()?;
    let profile = store
        .find_server_profile_by_id(server_profile_id)?
        .ok_or_else(|| CoreError::InvalidLink("server profile not found".to_string()))?;
    let credential = store
        .server_credential(&profile.id)?
        .ok_or_else(|| CoreError::Crypto("未找到已保存的登录信息，请重新登录".to_string()))?;
    let host = profile.link_host();
    let result = block_on_smb(async {
        smb_connection_for(Some(connection_id))?
            .connect_and_discover(
                connection_id,
                &host,
                &credential.username,
                &credential.password,
            )
            .await
    });
    if result.is_err() {
        remove_smb_connection(connection_id);
    }
    result?;
    Ok(())
}

fn disconnect_smb_connection(connection_id: &str) {
    if connection_id.trim().is_empty() {
        return;
    }
    let Some(connection) = take_smb_connection(connection_id) else {
        return;
    };
    let _ = block_on_smb(async {
        connection.disconnect().await;
        Ok::<_, String>(())
    });
}

fn remove_smb_connection(connection_id: &str) {
    let _ = take_smb_connection(connection_id);
}

fn take_smb_connection(connection_id: &str) -> Option<crate::smb_client::SmbConnection> {
    let normalized = crate::smb_client::normalize_connection_id(Some(connection_id));
    if let Ok(mut connections) = smb_connections_cell().write() {
        connections.remove(&normalized)
    } else {
        None
    }
}

fn store_cell() -> &'static RwLock<Option<crate::storage::CoreStore>> {
    CORE_STORE.get_or_init(|| RwLock::new(None))
}

fn set_app_store(store: crate::storage::CoreStore) -> CoreResult<()> {
    let mut guard = store_cell()
        .write()
        .map_err(|error| CoreError::Storage(error.to_string()))?;
    *guard = Some(store);
    Ok(())
}

fn app_store() -> CoreResult<crate::storage::CoreStore> {
    if let Some(store) = store_cell()
        .read()
        .map_err(|error| CoreError::Storage(error.to_string()))?
        .clone()
    {
        return Ok(store);
    }

    let store = crate::storage::CoreStore::in_memory()?;
    store.ensure_default_server_profile()?;
    set_app_store(store.clone())?;
    Ok(store)
}

fn bootstrap_state(store: &crate::storage::CoreStore) -> CoreResult<AppBootstrapState> {
    let server_profiles = store.list_server_profiles()?;
    let active_server = store
        .active_server_profile()?
        .or_else(|| server_profiles.first().cloned());
    if let Some(active_server) = active_server.as_ref() {
        store.set_active_server_profile(&active_server.id)?;
    }
    let active_credential = store
        .active_server_credential()?
        .map(ServerCredentialSummary::from);
    Ok(AppBootstrapState {
        server_profiles,
        active_server,
        active_credential,
    })
}

fn block_on_smb<F, T, E>(future: F) -> CoreResult<T>
where
    F: std::future::Future<Output = Result<T, E>>,
    E: IntoSmbCoreError,
{
    let runtime = SMB_RUNTIME
        .get_or_init(|| tokio::runtime::Runtime::new().map_err(|error| error.to_string()))
        .as_ref()
        .map_err(|error| CoreError::Storage(format!("SMB runtime unavailable: {error}")))?;
    runtime
        .block_on(future)
        .map_err(IntoSmbCoreError::into_core_error)
}

trait IntoSmbCoreError {
    fn into_core_error(self) -> CoreError;
}

impl IntoSmbCoreError for String {
    fn into_core_error(self) -> CoreError {
        CoreError::smb(self)
    }
}

impl IntoSmbCoreError for (String, SmbErrorCode) {
    fn into_core_error(self) -> CoreError {
        CoreError::smb_with_code(self.0, self.1)
    }
}

impl IntoSmbCoreError for crate::smb_client::SmbOperationError {
    fn into_core_error(self) -> CoreError {
        CoreError::smb_with_code(self.message, self.code)
    }
}

fn clear_operation_after<T>(
    operation_id: Option<&str>,
    operation: impl FnOnce() -> CoreResult<T>,
) -> CoreResult<T> {
    let result = operation();
    crate::smb_client::clear_operation(operation_id);
    result
}

fn clear_operation_result<T>(operation_id: Option<&str>, result: CoreResult<T>) -> CoreResult<T> {
    crate::smb_client::clear_operation(operation_id);
    result
}

fn error_code(error: &CoreError) -> &'static str {
    match error {
        CoreError::MissingField(_)
        | CoreError::InvalidLink(_)
        | CoreError::Url(_)
        | CoreError::Json(_) => "invalid_request",
        CoreError::Crypto(_) => "credential",
        CoreError::Storage(_) => "storage",
        CoreError::Smb { message, code } => {
            if *code == SmbErrorCode::Smb {
                classify_smb_error(message)
            } else {
                code.as_str()
            }
        }
    }
}

fn classify_smb_error(message: &str) -> &'static str {
    let lower = message.to_lowercase();
    if lower.contains("操作已取消") || lower.contains("cancel") || lower.contains("interrupted")
    {
        "cancelled"
    } else if lower.contains("auth")
        || lower.contains("logon")
        || lower.contains("password")
        || lower.contains("账号")
        || lower.contains("密码")
        || lower.contains("登录")
    {
        "auth"
    } else if lower.contains("未连接")
        || lower.contains("disconnect")
        || lower.contains("session")
        || lower.contains("expired")
        || lower.contains("network")
        || lower.contains("连接")
    {
        "reconnectable"
    } else if lower.contains("access") || lower.contains("denied") || lower.contains("permission") {
        "permission"
    } else if lower.contains("already exists")
        || lower.contains("file exists")
        || lower.contains("目标已存在")
        || lower.contains("已存在")
    {
        "already_exists"
    } else if lower.contains("not found") || lower.contains("找不到") {
        "not_found"
    } else {
        "smb"
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::preview::{PreviewContentType, PreviewKind};
    use crate::session::LinkActivation;
    use std::sync::{Mutex, OnceLock};
    use std::time::{Duration, SystemTime, UNIX_EPOCH};

    #[test]
    fn bridge_generates_fixed_http_link() {
        let _guard = test_store_lock().lock().unwrap();
        let json = generate_link_json(
            r#"{"server_host":"nas.local","share":"Media","path":"/Movies/demo.mp4","kind":"file"}"#,
        )
        .unwrap();
        let response: BridgeResponse<QuickLink> = serde_json::from_str(&json).unwrap();
        let link = response.data.unwrap();

        assert!(response.ok);
        assert!(link.http_url.starts_with("http://127.0.0.1:19527/s?"));
        assert!(link.http_url.contains("t=file"));
        assert!(!link.http_url.contains("&n="));
    }

    #[test]
    fn bridge_builds_link_without_persisting_favorite() {
        let _guard = test_store_lock().lock().unwrap();
        let path = unique_temp_db_path("build-link-store");
        let _ = open_store_json(&format!(r#"{{"path":"{}"}}"#, path.display())).unwrap();

        let json = build_link_json(
            r#"{"server_host":"nas.local","share":"Media","path":"/Movies/demo.mp4","kind":"file"}"#,
        )
        .unwrap();
        let response: BridgeResponse<QuickLink> = serde_json::from_str(&json).unwrap();
        let link = response.data.unwrap();
        let list_json = list_quick_links_json("{}").unwrap();
        let list_response: BridgeResponse<Vec<QuickLink>> =
            serde_json::from_str(&list_json).unwrap();

        assert!(response.ok);
        assert!(link.http_url.starts_with("http://127.0.0.1:19527/s?"));
        assert!(list_response.data.unwrap().is_empty());
    }

    #[test]
    fn bridge_activates_link_to_preview_plan() {
        let _guard = test_store_lock().lock().unwrap();
        let store = crate::storage::CoreStore::in_memory().unwrap();
        let profile = crate::server::ServerProfile::new(
            "NAS",
            "nas.local",
            Some("alice".to_string()),
            crate::server::AuthMode::UsernamePassword,
        )
        .unwrap();
        store.save_server_profile(&profile).unwrap();
        set_app_store(store).unwrap();

        let json = activate_link_json(
            r#"{"raw_link":"rynat://s?h=nas.local&s=Media&p=/Movies/demo.mp4&t=file"}"#,
        )
        .unwrap();
        let response: BridgeResponse<LinkActivation> = serde_json::from_str(&json).unwrap();
        let activation = response.data.unwrap();

        assert!(json.contains("\"content_type\":\"video\""));
        assert!(json.contains("\"kind\":\"video_poster\""));
        assert_eq!(activation.matched_server.as_ref().unwrap().id, profile.id);
        assert_eq!(activation.browse_location.remote_path, "/Movies");
        assert_eq!(
            activation.preview_plan.as_ref().unwrap().content_type,
            PreviewContentType::Video
        );
    }

    #[test]
    fn bridge_creates_preview_plan() {
        let _guard = test_store_lock().lock().unwrap();
        let json = preview_plan_json(
            r#"{"server_host":"nas.local","share":"Media","path":"/Photos/a.jpg","kind":"file","max_edge_px":512}"#,
        )
        .unwrap();
        let response: BridgeResponse<crate::PreviewPlan> = serde_json::from_str(&json).unwrap();
        let plan = response.data.unwrap();

        assert!(json.contains("\"content_type\":\"image\""));
        assert!(json.contains("\"kind\":\"image_thumbnail\""));
        assert_eq!(plan.content_type, PreviewContentType::Image);
        assert_eq!(plan.thumbnail.unwrap().kind, PreviewKind::ImageThumbnail);
        assert!(plan.playback.is_none());
    }

    #[test]
    fn c_bridge_returns_structured_error() {
        let _guard = test_store_lock().lock().unwrap();
        let input = CString::new("{}").unwrap();
        let output = rynat_generate_link_json(input.as_ptr());
        assert!(!output.is_null());
        let text = unsafe { CStr::from_ptr(output) }
            .to_str()
            .unwrap()
            .to_string();
        unsafe { rynat_free_string(output) };

        assert!(text.contains("\"ok\":false"));
        assert!(text.contains("missing field"));
    }

    #[test]
    fn bridge_creates_upload_plan_without_ui_memory_copy() {
        let _guard = test_store_lock().lock().unwrap();
        let json = upload_plan_json(
            r#"{"local_path":"/Users/a/Desktop/demo.mp4","server_host":"nas.local","share":"Media","remote_path":"/Movies/demo.mp4"}"#,
        )
        .unwrap();

        assert!(json.contains("\"direction\":\"upload\""));
        assert!(json.contains("\"requires_streaming\":true"));
        assert!(json.contains("\"allow_ui_memory_copy\":false"));
    }

    #[test]
    fn smb_list_requires_active_connection() {
        let _guard = test_store_lock().lock().unwrap();
        let input = CString::new(r#"{"share":"Media","path":"/"}"#).unwrap();
        let output = rynat_smb_list_directory_json(input.as_ptr());
        assert!(!output.is_null());
        let json = unsafe { CStr::from_ptr(output) }
            .to_str()
            .unwrap()
            .to_string();
        unsafe { rynat_free_string(output) };

        let response: BridgeResponse<Vec<crate::smb_client::SmbFileItem>> =
            serde_json::from_str(&json).unwrap();

        assert!(!response.ok);
        assert!(response.error.unwrap().contains("未连接"));
    }

    #[test]
    fn smb_diagnostics_accepts_explicit_connection_id_without_login() {
        let _guard = test_store_lock().lock().unwrap();
        let json = smb_diagnostics_json(r#"{"connection_id":"server-a"}"#).unwrap();
        let response: BridgeResponse<crate::smb_client::SmbDiagnostics> =
            serde_json::from_str(&json).unwrap();
        let diagnostics = response.data.unwrap();

        assert!(response.ok);
        assert!(!diagnostics.connected);
        assert_eq!(diagnostics.connection_id.as_deref(), Some("server-a"));
    }

    #[test]
    fn smb_scoped_requests_keep_legacy_default_connection_id() {
        let _guard = test_store_lock().lock().unwrap();
        let json = smb_diagnostics_json("{}").unwrap();
        let response: BridgeResponse<crate::smb_client::SmbDiagnostics> =
            serde_json::from_str(&json).unwrap();
        let diagnostics = response.data.unwrap();

        assert!(response.ok);
        assert_eq!(
            diagnostics.connection_id.as_deref(),
            Some(crate::smb_client::DEFAULT_CONNECTION_ID)
        );
    }

    #[test]
    fn bridge_opens_store_with_default_server() {
        let _guard = test_store_lock().lock().unwrap();
        let path = unique_temp_db_path("default-store");
        let json = open_store_json(&format!(r#"{{"path":"{}"}}"#, path.display())).unwrap();
        let response: BridgeResponse<AppBootstrapState> = serde_json::from_str(&json).unwrap();
        let state = response.data.unwrap();

        assert!(response.ok);
        assert_eq!(state.server_profiles.len(), 1);
        assert_eq!(
            state.active_server.as_ref().unwrap().display_name,
            crate::DEFAULT_SERVER_DISPLAY_NAME
        );
        assert_eq!(
            state.active_server.unwrap().endpoint.as_link_host(),
            crate::DEFAULT_SERVER_HOST
        );
    }

    #[test]
    fn bridge_saves_profile_and_credential() {
        let _guard = test_store_lock().lock().unwrap();
        let path = unique_temp_db_path("profile-store");
        let _ = open_store_json(&format!(r#"{{"path":"{}"}}"#, path.display())).unwrap();

        let profile_json = save_server_profile_json(
            r#"{"display_name":"测试 NAS","host":"nas.local","username":"alice","set_active":true}"#,
        )
        .unwrap();
        let profile_response: BridgeResponse<ServerProfile> =
            serde_json::from_str(&profile_json).unwrap();
        let profile = profile_response.data.unwrap();

        let credential_json = save_server_credential_json(&format!(
            r#"{{
                "server_profile_id":"{}",
                "username":"alice",
                "password":"secret",
                "remember_password":true,
                "auto_login":true
            }}"#,
            profile.id
        ))
        .unwrap();
        let credential_response: BridgeResponse<ServerCredentialSummary> =
            serde_json::from_str(&credential_json).unwrap();
        let credential = credential_response.data.unwrap();

        let state_json = app_bootstrap_json("{}").unwrap();
        let state_response: BridgeResponse<AppBootstrapState> =
            serde_json::from_str(&state_json).unwrap();
        let state = state_response.data.unwrap();

        assert_eq!(profile.display_name, "测试 NAS");
        assert_eq!(credential.username, "alice");
        let active_credential = state.active_credential.unwrap();
        assert_eq!(active_credential.username, "alice");
        assert!(active_credential.auto_login);
        assert_eq!(state.active_server.unwrap().id, profile.id);
    }

    #[test]
    fn bridge_deletes_server_profile_and_returns_updated_bootstrap() {
        let _guard = test_store_lock().lock().unwrap();
        let path = unique_temp_db_path("delete-profile-store");
        let _ = open_store_json(&format!(r#"{{"path":"{}"}}"#, path.display())).unwrap();

        let first_json = save_server_profile_json(
            r#"{"display_name":"设计部 NAS","host":"nas-a.local","username":"alice","set_active":true}"#,
        )
        .unwrap();
        let first_response: BridgeResponse<ServerProfile> =
            serde_json::from_str(&first_json).unwrap();
        let first = first_response.data.unwrap();
        let second_json = save_server_profile_json(
            r#"{"display_name":"归档 NAS","host":"nas-b.local","username":"bob","set_active":true}"#,
        )
        .unwrap();
        let second_response: BridgeResponse<ServerProfile> =
            serde_json::from_str(&second_json).unwrap();
        let second = second_response.data.unwrap();

        let delete_json =
            delete_server_profile_json(&format!(r#"{{"id":"{}"}}"#, second.id)).unwrap();
        let response: BridgeResponse<AppBootstrapState> =
            serde_json::from_str(&delete_json).unwrap();
        let state = response.data.unwrap();

        assert_eq!(state.server_profiles.len(), 2);
        assert!(
            state
                .server_profiles
                .iter()
                .any(|profile| profile.id == first.id)
        );
        assert!(
            state
                .server_profiles
                .iter()
                .all(|profile| profile.id != second.id)
        );
        let active_id = state.active_server.unwrap().id;
        assert_ne!(active_id, second.id);
        assert!(
            state
                .server_profiles
                .iter()
                .any(|profile| profile.id == active_id)
        );
    }

    #[test]
    fn bridge_bootstrap_does_not_return_plaintext_password() {
        let _guard = test_store_lock().lock().unwrap();
        let path = unique_temp_db_path("credential-summary-store");
        let _ = open_store_json(&format!(r#"{{"path":"{}"}}"#, path.display())).unwrap();

        let profile_json = save_server_profile_json(
            r#"{"display_name":"测试 NAS","host":"nas.local","username":"alice","set_active":true}"#,
        )
        .unwrap();
        let profile_response: BridgeResponse<ServerProfile> =
            serde_json::from_str(&profile_json).unwrap();
        let profile = profile_response.data.unwrap();

        let _ = save_server_credential_json(&format!(
            r#"{{
                "server_profile_id":"{}",
                "username":"alice",
                "password":"secret",
                "remember_password":true,
                "auto_login":false
            }}"#,
            profile.id
        ))
        .unwrap();

        let state_json = app_bootstrap_json("{}").unwrap();
        let state_response: BridgeResponse<AppBootstrapState> =
            serde_json::from_str(&state_json).unwrap();
        let credential = state_response.data.unwrap().active_credential.unwrap();

        assert_eq!(credential.username, "alice");
        assert!(!state_json.contains("secret"));
        assert!(!state_json.contains("\"password\""));
    }

    #[test]
    fn bridge_updates_credential_options_without_replacing_password() {
        let _guard = test_store_lock().lock().unwrap();
        let path = unique_temp_db_path("credential-options-store");
        let _ = open_store_json(&format!(r#"{{"path":"{}"}}"#, path.display())).unwrap();

        let profile_json = save_server_profile_json(
            r#"{"display_name":"测试 NAS","host":"nas.local","username":"alice","set_active":true}"#,
        )
        .unwrap();
        let profile_response: BridgeResponse<ServerProfile> =
            serde_json::from_str(&profile_json).unwrap();
        let profile = profile_response.data.unwrap();

        let _ = save_server_credential_json(&format!(
            r#"{{
                "server_profile_id":"{}",
                "username":"alice",
                "password":"secret",
                "remember_password":true,
                "auto_login":false
            }}"#,
            profile.id
        ))
        .unwrap();

        let updated_json = update_server_credential_options_json(&format!(
            r#"{{
                "server_profile_id":"{}",
                "remember_password":true,
                "auto_login":true
            }}"#,
            profile.id
        ))
        .unwrap();
        let updated_response: BridgeResponse<Option<ServerCredentialSummary>> =
            serde_json::from_str(&updated_json).unwrap();
        assert!(updated_response.data.unwrap().unwrap().auto_login);

        let store = app_store().unwrap();
        let credential = store.server_credential(&profile.id).unwrap().unwrap();
        assert_eq!(credential.password, "secret");
        assert!(credential.remember_password);
        assert!(credential.auto_login);
    }

    #[test]
    fn save_credential_with_remember_password_false_deletes_existing_password() {
        let _guard = test_store_lock().lock().unwrap();
        let path = unique_temp_db_path("credential-forget-store");
        let _ = open_store_json(&format!(r#"{{"path":"{}"}}"#, path.display())).unwrap();

        let profile_json = save_server_profile_json(
            r#"{"display_name":"测试 NAS","host":"nas.local","username":"alice","set_active":true}"#,
        )
        .unwrap();
        let profile_response: BridgeResponse<ServerProfile> =
            serde_json::from_str(&profile_json).unwrap();
        let profile = profile_response.data.unwrap();

        let _ = save_server_credential_json(&format!(
            r#"{{
                "server_profile_id":"{}",
                "username":"alice",
                "password":"secret",
                "remember_password":true,
                "auto_login":true
            }}"#,
            profile.id
        ))
        .unwrap();
        let _ = save_server_credential_json(&format!(
            r#"{{
                "server_profile_id":"{}",
                "username":"alice",
                "password":"replacement-should-not-persist",
                "remember_password":false,
                "auto_login":false
            }}"#,
            profile.id
        ))
        .unwrap();

        let store = app_store().unwrap();
        assert!(store.server_credential(&profile.id).unwrap().is_none());
    }

    #[test]
    fn smb_connect_stored_credential_requires_saved_credential() {
        let _guard = test_store_lock().lock().unwrap();
        let path = unique_temp_db_path("missing-credential-store");
        let _ = open_store_json(&format!(r#"{{"path":"{}"}}"#, path.display())).unwrap();
        let profile_json = save_server_profile_json(
            r#"{"display_name":"测试 NAS","host":"nas.local","username":"alice","set_active":true}"#,
        )
        .unwrap();
        let profile_response: BridgeResponse<ServerProfile> =
            serde_json::from_str(&profile_json).unwrap();
        let profile = profile_response.data.unwrap();

        let input = CString::new(format!(r#"{{"server_profile_id":"{}"}}"#, profile.id)).unwrap();
        let output = rynat_smb_connect_stored_credential_json(input.as_ptr());
        assert!(!output.is_null());
        let error_json = unsafe { CStr::from_ptr(output) }
            .to_str()
            .unwrap()
            .to_string();
        unsafe { rynat_free_string(output) };
        let response: BridgeResponse<serde_json::Value> =
            serde_json::from_str(&error_json).unwrap();

        assert!(!response.ok);
        assert_eq!(response.error_code.as_deref(), Some("credential"));
    }

    #[test]
    fn isolated_task_requires_server_profile_and_does_not_fallback_to_default_connection() {
        let missing_profile = smb_start_task_json(
            r#"{
                "operation":"test_noop",
                "payload":{"label":"ok"},
                "operation_id":"isolated-no-profile",
                "use_isolated_connection":true
            }"#,
        )
        .unwrap_err();
        assert!(matches!(
            missing_profile,
            CoreError::MissingField("server_profile_id")
        ));

        let _guard = test_store_lock().lock().unwrap();
        let path = unique_temp_db_path("isolated-missing-credential-store");
        let _ = open_store_json(&format!(r#"{{"path":"{}"}}"#, path.display())).unwrap();
        let profile_json = save_server_profile_json(
            r#"{"display_name":"测试 NAS","host":"nas.local","username":"alice","set_active":true}"#,
        )
        .unwrap();
        let profile_response: BridgeResponse<ServerProfile> =
            serde_json::from_str(&profile_json).unwrap();
        let profile = profile_response.data.unwrap();

        let missing_credential_json = smb_start_task_json(&format!(
            r#"{{
                "operation":"test_noop",
                "payload":{{"label":"ok"}},
                "operation_id":"isolated-no-credential",
                "server_profile_id":"{}",
                "use_isolated_connection":true
            }}"#,
            profile.id
        ))
        .unwrap();
        let missing_credential_response: BridgeResponse<SmbTaskStartResult> =
            serde_json::from_str(&missing_credential_json).unwrap();
        let missing_credential = missing_credential_response.data.unwrap();

        assert_eq!(missing_credential.state, SmbTaskState::Queued);
        let status = poll_task_until_done(&missing_credential.task_id);
        assert_eq!(status.state, SmbTaskState::Failed);
        assert_eq!(status.error_code.as_deref(), Some("credential"));
    }

    #[test]
    fn isolated_task_missing_credential_fails_in_task_status() {
        let _guard = test_store_lock().lock().unwrap();
        let path = unique_temp_db_path("isolated-task-async-failure-store");
        let _ = open_store_json(&format!(r#"{{"path":"{}"}}"#, path.display())).unwrap();
        let profile_json = save_server_profile_json(
            r#"{"display_name":"测试 NAS","host":"nas.local","username":"alice","set_active":true}"#,
        )
        .unwrap();
        let profile_response: BridgeResponse<ServerProfile> =
            serde_json::from_str(&profile_json).unwrap();
        let profile = profile_response.data.unwrap();

        let start_json = smb_start_task_json(&format!(
            r#"{{
                "operation":"test_noop",
                "payload":{{"label":"ok"}},
                "operation_id":"isolated-no-credential-async",
                "server_profile_id":"{}",
                "use_isolated_connection":true
            }}"#,
            profile.id
        ))
        .unwrap();
        let start_response: BridgeResponse<SmbTaskStartResult> =
            serde_json::from_str(&start_json).unwrap();
        let start = start_response.data.unwrap();

        assert_eq!(start.state, SmbTaskState::Queued);
        let status = poll_task_until_done(&start.task_id);
        assert_eq!(status.state, SmbTaskState::Failed);
        assert_eq!(status.error_code.as_deref(), Some("credential"));
        assert_eq!(status.connection_id, None);
    }

    #[test]
    fn clear_running_task_is_rejected() {
        let start_json = smb_start_task_json(
            r#"{
                "operation":"test_noop",
                "payload":{"label":"slow"},
                "operation_id":"task-op-clear-running",
                "use_isolated_connection":false
            }"#,
        )
        .unwrap();
        let start_response: BridgeResponse<SmbTaskStartResult> =
            serde_json::from_str(&start_json).unwrap();
        let start = start_response.data.unwrap();

        let clear_error =
            smb_clear_task_json(&format!(r#"{{"task_id":"{}"}}"#, start.task_id)).unwrap_err();

        assert!(matches!(clear_error, CoreError::InvalidLink(_)));
        let _ = smb_cancel_task_json(&format!(r#"{{"task_id":"{}"}}"#, start.task_id)).unwrap();
        let status = poll_task_until_done(&start.task_id);
        assert_eq!(status.state, SmbTaskState::Cancelled);
    }

    #[test]
    fn bridge_encrypts_and_decrypts_credentials() {
        let encrypted_json =
            encrypt_credential_json(r#"{"password":"secret-from-bridge"}"#).unwrap();
        let encrypted_response: BridgeResponse<String> =
            serde_json::from_str(&encrypted_json).unwrap();
        let encrypted = encrypted_response.data.unwrap();

        assert!(encrypted_response.ok);
        assert!(encrypted.starts_with("v1:"));
        assert!(!encrypted.contains("secret-from-bridge"));

        let decrypted_json =
            decrypt_credential_json(&format!(r#"{{"encrypted":"{}"}}"#, encrypted)).unwrap();
        let decrypted_response: BridgeResponse<String> =
            serde_json::from_str(&decrypted_json).unwrap();

        assert_eq!(
            decrypted_response.data.as_deref(),
            Some("secret-from-bridge")
        );
    }

    #[test]
    fn c_bridge_classifies_credential_errors() {
        let input = CString::new(r#"{"encrypted":"plaintext"}"#).unwrap();
        let output = rynat_decrypt_credential_json(input.as_ptr());
        assert!(!output.is_null());
        let text = unsafe { CStr::from_ptr(output) }
            .to_str()
            .unwrap()
            .to_string();
        unsafe { rynat_free_string(output) };

        let response: BridgeResponse<String> = serde_json::from_str(&text).unwrap();
        assert!(!response.ok);
        assert_eq!(response.error_code.as_deref(), Some("credential"));
    }

    #[test]
    fn uses_structured_auth_error_code() {
        assert_eq!(
            error_code(&CoreError::smb_with_code("登录失败", SmbErrorCode::Auth)),
            "auth"
        );
    }

    #[test]
    fn classifies_localized_auth_errors_as_fallback() {
        assert_eq!(
            error_code(&CoreError::smb("账号或密码错误，请检查后重试")),
            "auth"
        );
        assert_eq!(
            error_code(&CoreError::smb("SMB 连接失败: STATUS_LOGON_FAILURE")),
            "auth"
        );
    }

    #[test]
    fn classifies_existing_target_errors() {
        assert_eq!(
            error_code(&CoreError::smb("目标已存在 '/demo.txt'")),
            "already_exists"
        );
        assert_eq!(
            error_code(&CoreError::smb("remote file exists")),
            "already_exists"
        );
    }

    #[test]
    fn bridge_lists_and_deletes_generated_links() {
        let _guard = test_store_lock().lock().unwrap();
        let path = unique_temp_db_path("link-store");
        let _ = open_store_json(&format!(r#"{{"path":"{}"}}"#, path.display())).unwrap();

        let generated = generate_link_json(
            r#"{"server_host":"nas.local","share":"Media","path":"/Movies/demo.mp4","kind":"file"}"#,
        )
        .unwrap();
        let generated_response: BridgeResponse<QuickLink> =
            serde_json::from_str(&generated).unwrap();
        let link = generated_response.data.unwrap();

        let list_json = list_quick_links_json("{}").unwrap();
        let list_response: BridgeResponse<Vec<QuickLink>> =
            serde_json::from_str(&list_json).unwrap();
        let links = list_response.data.unwrap();

        assert_eq!(links.len(), 1);
        assert_eq!(links[0].http_url, link.http_url);

        let delete_json = delete_quick_link_json(&format!(r#"{{"id":"{}"}}"#, link.id)).unwrap();
        let delete_response: BridgeResponse<bool> = serde_json::from_str(&delete_json).unwrap();
        assert_eq!(delete_response.data, Some(true));

        let list_json = list_quick_links_json("{}").unwrap();
        let list_response: BridgeResponse<Vec<QuickLink>> =
            serde_json::from_str(&list_json).unwrap();
        assert!(list_response.data.unwrap().is_empty());
    }

    #[test]
    fn smb_task_lifecycle_completes_and_clears() {
        let start_json = smb_start_task_json(
            r#"{
                "operation":"test_noop",
                "payload":{"label":"ok"},
                "operation_id":"task-op-1",
                "use_isolated_connection":false
            }"#,
        )
        .unwrap();
        let start_response: BridgeResponse<SmbTaskStartResult> =
            serde_json::from_str(&start_json).unwrap();
        let start = start_response.data.unwrap();

        assert!(start_response.ok);
        assert_eq!(start.operation_id, "task-op-1");

        let status = poll_task_until_done(&start.task_id);
        assert_eq!(status.state, SmbTaskState::Succeeded);
        assert_eq!(
            status
                .data
                .as_ref()
                .and_then(|value| value.get("operation_id"))
                .and_then(|value| value.as_str()),
            Some("task-op-1")
        );

        let clear_json =
            smb_clear_task_json(&format!(r#"{{"task_id":"{}"}}"#, start.task_id)).unwrap();
        let clear_response: BridgeResponse<bool> = serde_json::from_str(&clear_json).unwrap();
        assert_eq!(clear_response.data, Some(true));

        let missing = smb_poll_task_json(&format!(r#"{{"task_id":"{}"}}"#, start.task_id));
        assert!(missing.is_err());
    }

    #[test]
    fn smb_task_cancel_marks_running_task_cancelled() {
        let start_json = smb_start_task_json(
            r#"{
                "operation":"test_noop",
                "payload":{"label":"slow"},
                "operation_id":"task-op-cancel",
                "use_isolated_connection":false
            }"#,
        )
        .unwrap();
        let start_response: BridgeResponse<SmbTaskStartResult> =
            serde_json::from_str(&start_json).unwrap();
        let start = start_response.data.unwrap();

        let cancel_json =
            smb_cancel_task_json(&format!(r#"{{"task_id":"{}"}}"#, start.task_id)).unwrap();
        let cancel_response: BridgeResponse<SmbTaskStatus> =
            serde_json::from_str(&cancel_json).unwrap();
        assert_eq!(cancel_response.data.unwrap().state, SmbTaskState::Cancelled);

        let status = poll_task_until_done(&start.task_id);
        assert_eq!(status.state, SmbTaskState::Cancelled);
        assert_eq!(status.error_code.as_deref(), Some("cancelled"));
    }

    fn unique_temp_db_path(prefix: &str) -> std::path::PathBuf {
        let nanos = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        std::env::temp_dir().join(format!("rynat-{prefix}-{nanos}.sqlite"))
    }

    fn test_store_lock() -> &'static Mutex<()> {
        static LOCK: OnceLock<Mutex<()>> = OnceLock::new();
        LOCK.get_or_init(|| Mutex::new(()))
    }

    fn poll_task_until_done(task_id: &str) -> SmbTaskStatus {
        for _ in 0..50 {
            let json = smb_poll_task_json(&format!(r#"{{"task_id":"{}"}}"#, task_id)).unwrap();
            let response: BridgeResponse<SmbTaskStatus> = serde_json::from_str(&json).unwrap();
            let status = response.data.unwrap();
            if matches!(
                status.state,
                SmbTaskState::Succeeded | SmbTaskState::Failed | SmbTaskState::Cancelled
            ) {
                return status;
            }
            std::thread::sleep(Duration::from_millis(20));
        }
        panic!("task did not finish in time");
    }
}
