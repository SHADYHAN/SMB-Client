use std::collections::HashMap;
use std::fmt::Display;
use std::future::Future;
use std::io::{Read, Write};
use std::path::PathBuf;
use std::sync::Arc;
use std::sync::{Mutex as StdMutex, MutexGuard, OnceLock};
use std::time::Duration;

use serde::{Deserialize, Serialize};
use smb2::ErrorKind;
use smb2::msg::close::CloseRequest;
use smb2::msg::create::{
    CreateDisposition, CreateRequest, CreateResponse, ImpersonationLevel, ShareAccess,
};
use smb2::msg::flush::FlushRequest;
use smb2::msg::ioctl::{FSCTL_SRV_COPYCHUNK, IoctlRequest, IoctlResponse, SMB2_0_IOCTL_IS_FSCTL};
use smb2::pack::{ReadCursor, Unpack};
use smb2::types::flags::FileAccessMask;
use smb2::types::status::NtStatus;
use smb2::types::{Command, FileId, OplockLevel};
use tokio::sync::{Mutex, RwLock};
use uuid::Uuid;

use crate::error::SmbErrorCode;

const FILE_NON_DIRECTORY_FILE: u32 = 0x0000_0040;
const FILE_ATTRIBUTE_NORMAL: u32 = 0x0000_0080;
const FSCTL_SRV_REQUEST_RESUME_KEY: u32 = 0x0014_0078;
const COPYCHUNK_BYTES: u64 = 1024 * 1024;
const CANCELLED_OPERATION_MESSAGE: &str = "操作已取消";
const SMB_CALL_TIMEOUT: Duration = Duration::from_secs(20);

static CANCELLED_OPERATIONS: OnceLock<StdMutex<std::collections::HashSet<String>>> =
    OnceLock::new();

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SmbConnectRequest {
    pub host: String,
    pub username: String,
    pub password: String,
    #[serde(default)]
    pub connection_id: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SmbConnectResult {
    pub connection_id: String,
    pub host: String,
    pub dialect_label: String,
    pub shares: Vec<DiscoveredShare>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SmbListDirectoryRequest {
    #[serde(default)]
    pub connection_id: Option<String>,
    pub share: String,
    pub path: String,
    #[serde(default)]
    pub operation_id: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SmbCacheFileRequest {
    #[serde(default)]
    pub connection_id: Option<String>,
    pub share: String,
    pub path: String,
    pub local_path: String,
    pub max_bytes: Option<u64>,
    pub operation_id: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SmbCachedFile {
    pub local_path: String,
    pub size: u64,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SmbUploadFileRequest {
    #[serde(default)]
    pub connection_id: Option<String>,
    pub share: String,
    pub local_path: String,
    pub remote_path: String,
    pub replace_existing: bool,
    pub operation_id: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SmbCreateDirectoryRequest {
    #[serde(default)]
    pub connection_id: Option<String>,
    pub share: String,
    pub path: String,
    #[serde(default)]
    pub operation_id: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SmbRenameRequest {
    #[serde(default)]
    pub connection_id: Option<String>,
    pub share: String,
    pub from_path: String,
    pub to_path: String,
    #[serde(default)]
    pub operation_id: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SmbCopyFileRequest {
    #[serde(default)]
    pub connection_id: Option<String>,
    pub source_share: String,
    pub source_path: String,
    pub target_share: String,
    pub target_path: String,
    pub replace_existing: bool,
    pub operation_id: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SmbDeleteRequest {
    #[serde(default)]
    pub connection_id: Option<String>,
    pub share: String,
    pub path: String,
    pub is_dir: bool,
    #[serde(default)]
    pub operation_id: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum SmbCopyMethod {
    ServerSide,
    StreamedFallback,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SmbWriteResult {
    pub path: String,
    pub size: u64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub copy_method: Option<SmbCopyMethod>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub copy_fallback_reason: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SmbDiagnostics {
    pub connected: bool,
    pub connection_id: Option<String>,
    pub host: Option<String>,
    pub cached_share_count: usize,
    pub last_copy_method: Option<SmbCopyMethod>,
    pub last_copy_fallback_reason: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SmbCancelOperationRequest {
    pub operation_id: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DiscoveredShare {
    pub name: String,
    pub comment: String,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SmbOperationError {
    pub message: String,
    pub code: SmbErrorCode,
}

impl SmbOperationError {
    fn new(message: impl Into<String>, code: SmbErrorCode) -> Self {
        Self {
            message: message.into(),
            code,
        }
    }
}

impl From<String> for SmbOperationError {
    fn from(message: String) -> Self {
        Self::new(message, SmbErrorCode::Smb)
    }
}

impl From<&str> for SmbOperationError {
    fn from(message: &str) -> Self {
        Self::new(message, SmbErrorCode::Smb)
    }
}

pub const DEFAULT_CONNECTION_ID: &str = "default";

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SmbFileItem {
    pub name: String,
    pub path: String,
    pub is_dir: bool,
    pub size: u64,
    pub modified_time: Option<i64>,
}

type LastCopyStatus = Option<(SmbCopyMethod, Option<String>)>;

#[derive(Clone)]
pub struct SmbConnection {
    primary: Arc<Mutex<Option<smb2::SmbClient>>>,
    conn: Arc<RwLock<Option<smb2::client::connection::Connection>>>,
    trees: Arc<Mutex<HashMap<String, smb2::Tree>>>,
    host: Arc<RwLock<Option<String>>>,
    last_copy: Arc<RwLock<LastCopyStatus>>,
}

impl Default for SmbConnection {
    fn default() -> Self {
        Self::new()
    }
}

impl SmbConnection {
    pub fn new() -> Self {
        Self {
            primary: Arc::new(Mutex::new(None)),
            conn: Arc::new(RwLock::new(None)),
            trees: Arc::new(Mutex::new(HashMap::new())),
            host: Arc::new(RwLock::new(None)),
            last_copy: Arc::new(RwLock::new(None)),
        }
    }

    pub async fn connect_and_discover(
        &self,
        connection_id: &str,
        host: &str,
        username: &str,
        password: &str,
    ) -> Result<SmbConnectResult, SmbOperationError> {
        let endpoint = normalize_smb_endpoint(host);
        if endpoint.is_empty() {
            return Err(SmbOperationError::new(
                "请填写服务器地址",
                SmbErrorCode::Smb,
            ));
        }
        let normalized_host = profile_host_from_endpoint(&endpoint);

        let mut client = await_smb_call(
            "连接服务器",
            None,
            smb2::connect(&endpoint, username, password),
        )
        .await
        .map_err(classify_connect_error)?;

        let shares = await_smb_call("获取共享列表", None, client.list_shares())
            .await
            .map_err(|error| {
                SmbOperationError::new(format!("获取共享列表失败: {}", error), SmbErrorCode::Smb)
            })?;

        let conn_handle = client.connection_mut().clone();
        {
            let mut conn_w = self.conn.write().await;
            *conn_w = Some(conn_handle);
        }
        {
            let mut primary = self.primary.lock().await;
            *primary = Some(client);
        }
        self.trees.lock().await.clear();
        {
            let mut host_w = self.host.write().await;
            *host_w = Some(normalized_host.clone());
        }

        let mut discovered: Vec<DiscoveredShare> = shares
            .into_iter()
            .filter(|share| !share.name.starts_with('$'))
            .map(|share| DiscoveredShare {
                name: share.name,
                comment: share.comment,
            })
            .collect();

        discovered.sort_by_key(|item| item.name.to_lowercase());

        if discovered.is_empty() {
            return Err(SmbOperationError::new(
                "没有找到可访问的共享文件夹，请检查账号权限",
                SmbErrorCode::Permission,
            ));
        }

        Ok(SmbConnectResult {
            connection_id: normalize_connection_id(Some(connection_id)),
            host: normalized_host,
            dialect_label: "SMB3 自动".to_string(),
            shares: discovered,
        })
    }

    pub async fn disconnect(&self) {
        *self.conn.write().await = None;
        self.trees.lock().await.clear();
        *self.primary.lock().await = None;
        *self.host.write().await = None;
    }

    pub async fn diagnostics(&self, connection_id: Option<&str>) -> SmbDiagnostics {
        let last_copy = self.last_copy.read().await.clone();
        SmbDiagnostics {
            connected: self.conn.read().await.is_some(),
            connection_id: connection_id.map(|id| normalize_connection_id(Some(id))),
            host: self.host.read().await.clone(),
            cached_share_count: self.trees.lock().await.len(),
            last_copy_method: last_copy.as_ref().map(|value| value.0.clone()),
            last_copy_fallback_reason: last_copy.and_then(|value| value.1),
        }
    }

    pub async fn list_directory(
        &self,
        share: &str,
        path: &str,
        operation_id: Option<&str>,
    ) -> Result<Vec<SmbFileItem>, String> {
        ensure_not_cancelled(operation_id)?;
        let mut conn = self.clone_conn().await?;
        let tree = self.tree_for(share, operation_id).await?;
        let dir_path = to_smb_dir_path(path);

        let entries = await_smb_call(
            "读取目录",
            operation_id,
            tree.list_directory(&mut conn, &dir_path),
        )
        .await
        .map_err(|error| format!("无法读取目录 '{}': {}", path, error))?;
        ensure_not_cancelled(operation_id)?;

        let mut items: Vec<SmbFileItem> = entries
            .into_iter()
            .filter(|entry| {
                entry.name != "."
                    && entry.name != ".."
                    && !entry.name.starts_with('.')
                    && entry.name != "#recycle"
            })
            .map(|entry| {
                let full_path = if dir_path.is_empty() {
                    format!("/{}", entry.name)
                } else {
                    format!("/{}/{}", dir_path, entry.name)
                };
                let modified_time =
                    filetime_to_unix(entry.modified).or_else(|| filetime_to_unix(entry.created));
                SmbFileItem {
                    name: entry.name,
                    path: full_path,
                    is_dir: entry.is_directory,
                    size: entry.size,
                    modified_time,
                }
            })
            .collect();

        items.sort_by(|a, b| {
            if a.is_dir != b.is_dir {
                b.is_dir.cmp(&a.is_dir)
            } else {
                a.name.to_lowercase().cmp(&b.name.to_lowercase())
            }
        });

        Ok(items)
    }

    pub async fn cache_file(
        &self,
        share: &str,
        path: &str,
        local_path: &str,
        max_bytes: Option<u64>,
        operation_id: Option<&str>,
    ) -> Result<SmbCachedFile, String> {
        let mut conn = self.clone_conn().await?;
        let tree = self.tree_for(share, operation_id).await?;
        let file_path = path.trim_start_matches('/');
        if file_path.is_empty() {
            return Err("不能缓存共享根目录".to_string());
        }

        let destination = PathBuf::from(local_path);
        if let Some(parent) = destination.parent() {
            std::fs::create_dir_all(parent)
                .map_err(|error| format!("无法创建缓存目录: {}", error))?;
        }

        let mut download = await_smb_call(
            "开始读取文件",
            operation_id,
            tree.download(&mut conn, file_path),
        )
        .await
        .map_err(|error| format!("无法开始读取文件: {}", error))?;

        let temp_path = destination.with_extension(format!(
            "{}download",
            destination
                .extension()
                .and_then(|value| value.to_str())
                .map(|value| format!("{value}."))
                .unwrap_or_default()
        ));
        let mut file = std::fs::File::create(&temp_path)
            .map_err(|error| format!("无法创建缓存文件: {}", error))?;

        let mut bytes_written = 0_u64;
        let cache_result: Result<(), String> = async {
            loop {
                let chunk_result =
                    await_smb_optional_call("读取文件", operation_id, download.next_chunk())
                        .await?;
                let Some(chunk_result) = chunk_result else {
                    break;
                };
                let chunk = chunk_result.map_err(|error| format!("读取文件中断: {}", error))?;
                let write_slice = if let Some(limit) = max_bytes {
                    if bytes_written >= limit {
                        break;
                    }
                    let remaining = limit - bytes_written;
                    &chunk[..chunk.len().min(remaining as usize)]
                } else {
                    &chunk
                };
                file.write_all(write_slice)
                    .map_err(|error| format!("写入缓存失败: {}", error))?;
                bytes_written += write_slice.len() as u64;
                if max_bytes.is_some_and(|limit| bytes_written >= limit) {
                    break;
                }
            }
            file.flush()
                .map_err(|error| format!("刷新缓存文件失败: {}", error))?;
            Ok(())
        }
        .await;
        drop(file);

        if let Err(error) = cache_result {
            let _ = std::fs::remove_file(&temp_path);
            return Err(error);
        }

        if let Err(error) = std::fs::rename(&temp_path, &destination) {
            let _ = std::fs::remove_file(&temp_path);
            return Err(format!("保存缓存文件失败: {}", error));
        }

        Ok(SmbCachedFile {
            local_path: destination.to_string_lossy().to_string(),
            size: bytes_written,
        })
    }

    pub async fn upload_file(
        &self,
        share: &str,
        local_path: &str,
        remote_path: &str,
        replace_existing: bool,
        operation_id: Option<&str>,
    ) -> Result<SmbWriteResult, String> {
        let tree = Arc::new(self.tree_for(share, operation_id).await?);
        let file_path = to_smb_file_path(remote_path)?;
        let temp_path = temporary_remote_path(remote_path);
        let temp_file_path = to_smb_file_path(&temp_path)?;
        let mut file = std::fs::File::open(local_path)
            .map_err(|error| format!("无法打开本地文件 '{}': {}", local_path, error))?;
        let _ = self.delete_path(share, &temp_path, false, None).await;
        let write_conn = self.clone_conn().await?;
        let mut writer = match await_smb_call(
            "开始上传",
            operation_id,
            tree.create_file_writer(write_conn, &temp_file_path),
        )
        .await
        {
            Ok(writer) => writer,
            Err(error) => {
                let _ = self.delete_path(share, &temp_path, false, None).await;
                return Err(format!("上传文件失败 '{}': {}", remote_path, error));
            }
        };
        let mut buffer = vec![0_u8; crate::transfer::DEFAULT_TRANSFER_BUFFER_BYTES as usize];
        loop {
            ensure_not_cancelled(operation_id)?;
            let count = match file.read(&mut buffer) {
                Ok(0) => break,
                Ok(count) => count,
                Err(error) => {
                    let _ = writer.abort().await;
                    let _ = self.delete_path(share, &temp_path, false, None).await;
                    return Err(format!("读取本地文件失败 '{}': {}", local_path, error));
                }
            };
            if let Err(error) = await_smb_call(
                "上传文件",
                operation_id,
                writer.write_chunk(&buffer[..count]),
            )
            .await
            {
                let _ = writer.abort().await;
                let _ = self.delete_path(share, &temp_path, false, None).await;
                return Err(format!("上传文件失败 '{}': {}", remote_path, error));
            }
        }
        let bytes_written = match await_smb_call("完成上传", operation_id, writer.finish()).await
        {
            Ok(bytes_written) => bytes_written,
            Err(error) => {
                let _ = self.delete_path(share, &temp_path, false, None).await;
                return Err(format!("完成上传失败 '{}': {}", remote_path, error));
            }
        };
        if let Err(error) = ensure_not_cancelled(operation_id) {
            let _ = self.delete_path(share, &temp_path, false, None).await;
            return Err(error);
        }
        if let Err(error) = self
            .commit_temp_file(
                share,
                &temp_path,
                &file_path,
                replace_existing,
                operation_id,
            )
            .await
        {
            let _ = self.delete_path(share, &temp_path, false, None).await;
            return Err(format!("保存上传文件失败 '{}': {}", remote_path, error));
        }
        Ok(SmbWriteResult {
            path: normalize_remote_result_path(remote_path),
            size: bytes_written,
            copy_method: None,
            copy_fallback_reason: None,
        })
    }

    pub async fn create_directory(
        &self,
        share: &str,
        path: &str,
        operation_id: Option<&str>,
    ) -> Result<SmbWriteResult, String> {
        ensure_not_cancelled(operation_id)?;
        let mut conn = self.clone_conn().await?;
        let tree = self.tree_for(share, operation_id).await?;
        let dir_path = to_smb_file_path(path)?;
        await_smb_call(
            "创建文件夹",
            operation_id,
            tree.create_directory(&mut conn, &dir_path),
        )
        .await
        .map_err(|error| format!("创建文件夹失败 '{}': {}", path, error))?;
        ensure_not_cancelled(operation_id)?;
        Ok(SmbWriteResult {
            path: normalize_remote_result_path(path),
            size: 0,
            copy_method: None,
            copy_fallback_reason: None,
        })
    }

    pub async fn rename_path(
        &self,
        share: &str,
        from_path: &str,
        to_path: &str,
        operation_id: Option<&str>,
    ) -> Result<SmbWriteResult, String> {
        ensure_not_cancelled(operation_id)?;
        let mut conn = self.clone_conn().await?;
        let tree = self.tree_for(share, operation_id).await?;
        let from = to_smb_file_path(from_path)?;
        let to = to_smb_file_path(to_path)?;
        await_smb_call("重命名", operation_id, tree.rename(&mut conn, &from, &to))
            .await
            .map_err(|error| format!("重命名失败 '{}': {}", from_path, error))?;
        ensure_not_cancelled(operation_id)?;
        Ok(SmbWriteResult {
            path: normalize_remote_result_path(to_path),
            size: 0,
            copy_method: None,
            copy_fallback_reason: None,
        })
    }

    pub async fn copy_file(
        &self,
        source_share: &str,
        source_path: &str,
        target_share: &str,
        target_path: &str,
        replace_existing: bool,
        operation_id: Option<&str>,
    ) -> Result<SmbWriteResult, String> {
        let temp_target_path = temporary_remote_path(target_path);
        let _ = self
            .delete_path(target_share, &temp_target_path, false, None)
            .await;
        let server_copy_error = match self
            .copy_file_server_side(
                source_share,
                source_path,
                target_share,
                &temp_target_path,
                operation_id,
            )
            .await
        {
            Ok(bytes_written) => {
                self.commit_temp_file(
                    target_share,
                    &temp_target_path,
                    target_path,
                    replace_existing,
                    operation_id,
                )
                .await?;
                *self.last_copy.write().await = Some((SmbCopyMethod::ServerSide, None));
                return Ok(SmbWriteResult {
                    path: normalize_remote_result_path(target_path),
                    size: bytes_written,
                    copy_method: Some(SmbCopyMethod::ServerSide),
                    copy_fallback_reason: None,
                });
            }
            Err(error) => error,
        };

        let bytes_written = self
            .copy_file_streamed(
                source_share,
                source_path,
                target_share,
                &temp_target_path,
                operation_id,
            )
            .await?;
        self.commit_temp_file(
            target_share,
            &temp_target_path,
            target_path,
            replace_existing,
            operation_id,
        )
        .await?;
        *self.last_copy.write().await = Some((
            SmbCopyMethod::StreamedFallback,
            Some(server_copy_error.clone()),
        ));
        Ok(SmbWriteResult {
            path: normalize_remote_result_path(target_path),
            size: bytes_written,
            copy_method: Some(SmbCopyMethod::StreamedFallback),
            copy_fallback_reason: Some(server_copy_error),
        })
    }

    async fn copy_file_streamed(
        &self,
        source_share: &str,
        source_path: &str,
        target_share: &str,
        target_path: &str,
        operation_id: Option<&str>,
    ) -> Result<u64, String> {
        let mut read_conn = self.clone_conn().await?;
        let write_conn = self.clone_conn().await?;
        let source_tree = self.tree_for(source_share, operation_id).await?;
        let target_tree = Arc::new(self.tree_for(target_share, operation_id).await?);
        let source = to_smb_file_path(source_path)?;
        let target = to_smb_file_path(target_path)?;

        let mut download = await_smb_call(
            "复制文件读取",
            operation_id,
            source_tree.download(&mut read_conn, &source),
        )
        .await
        .map_err(|error| format!("复制文件失败，无法读取 '{}': {}", source_path, error))?;
        let mut writer = await_smb_call(
            "复制文件写入",
            operation_id,
            target_tree.create_file_writer(write_conn, &target),
        )
        .await
        .map_err(|error| format!("复制文件失败，无法写入 '{}': {}", target_path, error))?;

        loop {
            let chunk_result =
                await_smb_optional_call("复制文件读取", operation_id, download.next_chunk()).await;
            let chunk_result = match chunk_result {
                Ok(Some(chunk_result)) => chunk_result,
                Ok(None) => break,
                Err(error) => {
                    let _ = writer.abort().await;
                    let _ = self
                        .delete_path(target_share, target_path, false, None)
                        .await;
                    return Err(error);
                }
            };
            if let Err(error) = ensure_not_cancelled(operation_id) {
                let _ = writer.abort().await;
                let _ = self
                    .delete_path(target_share, target_path, false, None)
                    .await;
                return Err(error);
            }
            let chunk = match chunk_result {
                Ok(chunk) => chunk,
                Err(error) => {
                    let _ = writer.abort().await;
                    let _ = self
                        .delete_path(target_share, target_path, false, None)
                        .await;
                    return Err(format!("复制文件读取中断: {}", error));
                }
            };
            if let Err(error) =
                await_smb_call("复制文件写入", operation_id, writer.write_chunk(&chunk)).await
            {
                let _ = writer.abort().await;
                let _ = self
                    .delete_path(target_share, target_path, false, None)
                    .await;
                return Err(format!("复制文件写入中断: {}", error));
            }
        }

        let bytes_written = match await_smb_call("完成复制", operation_id, writer.finish()).await
        {
            Ok(bytes_written) => bytes_written,
            Err(error) => {
                let _ = self
                    .delete_path(target_share, target_path, false, None)
                    .await;
                return Err(format!("完成复制失败: {}", error));
            }
        };
        Ok(bytes_written)
    }

    async fn copy_file_server_side(
        &self,
        source_share: &str,
        source_path: &str,
        target_share: &str,
        target_path: &str,
        operation_id: Option<&str>,
    ) -> Result<u64, String> {
        let mut conn = self.clone_conn().await?;
        let source_tree = self.tree_for(source_share, operation_id).await?;
        let target_tree = self.tree_for(target_share, operation_id).await?;
        let source = to_smb_file_path(source_path)?;
        let target = to_smb_file_path(target_path)?;

        let (source_id, source_size) =
            open_source_for_server_copy(&mut conn, &source_tree, &source, operation_id)
                .await
                .map_err(|error| {
                    format!("服务端复制无法打开源文件 '{}': {}", source_path, error)
                })?;

        let target_id =
            match open_target_for_server_copy(&mut conn, &target_tree, &target, operation_id).await
            {
                Ok(file_id) => file_id,
                Err(error) => {
                    let _ =
                        close_smb_handle(&mut conn, &source_tree, source_id, operation_id).await;
                    return Err(format!(
                        "服务端复制无法创建目标文件 '{}': {}",
                        target_path, error
                    ));
                }
            };

        let copy_result = async {
            let resume_key =
                request_resume_key(&mut conn, &source_tree, source_id, operation_id).await?;
            copy_chunks_server_side(
                &mut conn,
                &target_tree,
                target_id,
                &resume_key,
                source_size,
                operation_id,
            )
            .await?;
            flush_smb_handle(&mut conn, &target_tree, target_id, operation_id).await?;
            Ok::<u64, String>(source_size)
        }
        .await;

        let close_target_result =
            close_smb_handle(&mut conn, &target_tree, target_id, operation_id).await;
        let close_source_result =
            close_smb_handle(&mut conn, &source_tree, source_id, operation_id).await;

        copy_result?;
        close_target_result?;
        close_source_result?;
        Ok(source_size)
    }

    pub async fn delete_path(
        &self,
        share: &str,
        path: &str,
        is_dir: bool,
        operation_id: Option<&str>,
    ) -> Result<SmbWriteResult, String> {
        ensure_not_cancelled(operation_id)?;
        let mut conn = self.clone_conn().await?;
        let tree = self.tree_for(share, operation_id).await?;
        let smb_path = to_smb_file_path(path)?;
        let result = if is_dir {
            await_smb_call(
                "删除文件夹",
                operation_id,
                tree.delete_directory(&mut conn, &smb_path),
            )
            .await
        } else {
            await_smb_call(
                "删除文件",
                operation_id,
                tree.delete_file(&mut conn, &smb_path),
            )
            .await
        };
        result.map_err(|error| {
            if is_dir {
                format!("删除文件夹失败 '{}': {}。文件夹需要为空。", path, error)
            } else {
                format!("删除文件失败 '{}': {}", path, error)
            }
        })?;
        ensure_not_cancelled(operation_id)?;
        Ok(SmbWriteResult {
            path: normalize_remote_result_path(path),
            size: 0,
            copy_method: None,
            copy_fallback_reason: None,
        })
    }

    async fn commit_temp_file(
        &self,
        share: &str,
        temp_path: &str,
        final_path: &str,
        replace_existing: bool,
        operation_id: Option<&str>,
    ) -> Result<(), String> {
        ensure_not_cancelled(operation_id)?;
        let mut backup_path: Option<String> = None;
        let target_exists = self.path_exists(share, final_path, operation_id).await?;
        if target_exists {
            if !replace_existing {
                let _ = self.delete_path(share, temp_path, false, None).await;
                return Err(format!("目标已存在 '{}'", final_path));
            }
            if self
                .path_is_directory(share, final_path, operation_id)
                .await?
            {
                let _ = self.delete_path(share, temp_path, false, None).await;
                return Err("目标是文件夹，不能直接替换".to_string());
            }
            let backup = temporary_remote_path(final_path);
            let _ = self.delete_path(share, &backup, false, None).await;
            if let Err(error) = self
                .rename_path(share, final_path, &backup, operation_id)
                .await
            {
                let _ = self.delete_path(share, temp_path, false, None).await;
                return Err(format!("无法准备替换目标: {}", error));
            }
            backup_path = Some(backup);
        }
        ensure_not_cancelled(operation_id)?;
        if let Err(error) = self
            .rename_path(share, temp_path, final_path, operation_id)
            .await
        {
            let _ = self.delete_path(share, temp_path, false, None).await;
            if let Some(backup) = backup_path.as_deref() {
                let _ = self.rename_path(share, backup, final_path, None).await;
            }
            return Err(format!("保存复制结果失败 '{}': {}", final_path, error));
        }
        if let Some(backup) = backup_path {
            let _ = self.delete_path(share, &backup, false, None).await;
        }
        Ok(())
    }

    async fn path_exists(
        &self,
        share: &str,
        path: &str,
        operation_id: Option<&str>,
    ) -> Result<bool, String> {
        let mut conn = self.clone_conn().await?;
        let tree = self.tree_for(share, operation_id).await?;
        let file_path = to_smb_file_path(path)?;
        match open_existing_path_for_probe(&mut conn, &tree, &file_path, operation_id).await {
            Ok(file_id) => {
                close_smb_handle(&mut conn, &tree, file_id, operation_id).await?;
                Ok(true)
            }
            Err(error) if error.kind() == ErrorKind::NotFound => Ok(false),
            Err(error) => Err(format!("检查目标是否存在失败 '{}': {}", path, error)),
        }
    }

    async fn path_is_directory(
        &self,
        share: &str,
        path: &str,
        operation_id: Option<&str>,
    ) -> Result<bool, String> {
        let parent = parent_remote_path(path);
        let name = remote_file_name(path);
        let items = self.list_directory(share, &parent, operation_id).await?;
        Ok(items
            .into_iter()
            .any(|item| item.name == name && item.is_dir))
    }

    async fn clone_conn(&self) -> Result<smb2::client::connection::Connection, String> {
        let guard = self.conn.read().await;
        guard
            .as_ref()
            .cloned()
            .ok_or_else(|| "未连接到服务器，请先登录".to_string())
    }

    async fn tree_for(
        &self,
        share: &str,
        operation_id: Option<&str>,
    ) -> Result<smb2::Tree, String> {
        let normalized_share = share.trim();
        if normalized_share.is_empty() {
            return Err("共享名为空".to_string());
        }

        if let Some(tree) = self.trees.lock().await.get(normalized_share).cloned() {
            return Ok(tree);
        }

        let tree = {
            let mut primary = self.primary.lock().await;
            let client = primary.as_mut().ok_or("未连接到服务器，请先登录")?;
            await_smb_call(
                "访问共享",
                operation_id,
                client.connect_share(normalized_share),
            )
            .await
            .map_err(|error| format!("无法访问共享 '{}': {}", normalized_share, error))?
        };
        let mut trees = self.trees.lock().await;
        if let Some(existing) = trees.get(normalized_share).cloned() {
            return Ok(existing);
        }
        trees.insert(normalized_share.to_string(), tree.clone());
        Ok(tree)
    }
}

pub fn normalize_connection_id(input: Option<&str>) -> String {
    let id = input.unwrap_or(DEFAULT_CONNECTION_ID).trim();
    if id.is_empty() {
        DEFAULT_CONNECTION_ID.to_string()
    } else {
        id.to_string()
    }
}

pub fn normalize_host(input: &str) -> String {
    let endpoint = normalize_smb_endpoint(input);
    endpoint
        .rsplit_once(':')
        .filter(|(_, port)| port.chars().all(|ch| ch.is_ascii_digit()))
        .map(|(host, _)| host.to_string())
        .unwrap_or(endpoint)
}

pub fn normalize_smb_endpoint(input: &str) -> String {
    let trimmed = input
        .trim()
        .trim_start_matches("smb://")
        .trim_start_matches("SMB://")
        .trim_start_matches("cifs://")
        .trim_start_matches("CIFS://")
        .trim_start_matches("\\\\")
        .trim_start_matches("//")
        .trim_end_matches('/')
        .trim_end_matches('\\')
        .replace('\\', "/");

    let host = trimmed.split('/').next().unwrap_or("").trim();
    if host.is_empty() {
        String::new()
    } else if host
        .rsplit_once(':')
        .is_some_and(|(_, port)| port.chars().all(|ch| ch.is_ascii_digit()))
    {
        host.to_string()
    } else {
        format!("{host}:445")
    }
}

fn profile_host_from_endpoint(endpoint: &str) -> String {
    if let Some((host, port)) = endpoint.rsplit_once(':')
        && port == "445"
    {
        return host.to_string();
    }
    endpoint.to_string()
}

fn to_smb_dir_path(path: &str) -> String {
    let trimmed = path.trim();
    if trimmed.is_empty() || trimmed == "/" {
        String::new()
    } else {
        trimmed
            .trim_start_matches('/')
            .trim_end_matches('/')
            .to_string()
    }
}

fn to_smb_file_path(path: &str) -> Result<String, String> {
    let trimmed = path.trim().trim_matches('/');
    if trimmed.is_empty() {
        return Err("路径不能为空".to_string());
    }
    Ok(trimmed.to_string())
}

fn normalize_remote_result_path(path: &str) -> String {
    let trimmed = path.trim().replace('\\', "/");
    if trimmed.is_empty() || trimmed == "/" {
        "/".to_string()
    } else if trimmed.starts_with('/') {
        trimmed
    } else {
        format!("/{trimmed}")
    }
}

async fn open_source_for_server_copy(
    conn: &mut smb2::client::connection::Connection,
    tree: &smb2::Tree,
    path: &str,
    operation_id: Option<&str>,
) -> Result<(FileId, u64), String> {
    let req = CreateRequest {
        requested_oplock_level: OplockLevel::None,
        impersonation_level: ImpersonationLevel::Impersonation,
        desired_access: FileAccessMask::new(
            FileAccessMask::FILE_READ_DATA
                | FileAccessMask::FILE_READ_ATTRIBUTES
                | FileAccessMask::SYNCHRONIZE,
        ),
        file_attributes: 0,
        share_access: ShareAccess(
            ShareAccess::FILE_SHARE_READ
                | ShareAccess::FILE_SHARE_WRITE
                | ShareAccess::FILE_SHARE_DELETE,
        ),
        create_disposition: CreateDisposition::FileOpen,
        create_options: 0,
        name: normalize_smb_path_for_copy(path),
        create_contexts: vec![],
    };
    let frame = await_smb_call(
        "打开源文件",
        operation_id,
        conn.execute(Command::Create, &req, Some(tree.tree_id)),
    )
    .await?;
    if frame.header.status != NtStatus::SUCCESS {
        return Err(format!("{:?}", frame.header.status));
    }
    let mut cursor = ReadCursor::new(&frame.body);
    let resp = CreateResponse::unpack(&mut cursor).map_err(|error| error.to_string())?;
    Ok((resp.file_id, resp.end_of_file))
}

async fn open_existing_path_for_probe(
    conn: &mut smb2::client::connection::Connection,
    tree: &smb2::Tree,
    path: &str,
    operation_id: Option<&str>,
) -> std::result::Result<FileId, smb2::Error> {
    let req = CreateRequest {
        requested_oplock_level: OplockLevel::None,
        impersonation_level: ImpersonationLevel::Impersonation,
        desired_access: FileAccessMask::new(FileAccessMask::FILE_READ_ATTRIBUTES),
        file_attributes: 0,
        share_access: ShareAccess(
            ShareAccess::FILE_SHARE_READ
                | ShareAccess::FILE_SHARE_WRITE
                | ShareAccess::FILE_SHARE_DELETE,
        ),
        create_disposition: CreateDisposition::FileOpen,
        create_options: 0,
        name: normalize_smb_path_for_copy(path),
        create_contexts: vec![],
    };
    let frame = await_smb_call(
        "检查目标",
        operation_id,
        conn.execute(Command::Create, &req, Some(tree.tree_id)),
    )
    .await
    .map_err(|error| smb2::Error::Io(std::io::Error::new(std::io::ErrorKind::TimedOut, error)))?;
    if frame.header.status != NtStatus::SUCCESS {
        return Err(smb2::Error::Protocol {
            status: frame.header.status,
            command: Command::Create,
        });
    }
    let mut cursor = ReadCursor::new(&frame.body);
    let resp = CreateResponse::unpack(&mut cursor)?;
    Ok(resp.file_id)
}

async fn open_target_for_server_copy(
    conn: &mut smb2::client::connection::Connection,
    tree: &smb2::Tree,
    path: &str,
    operation_id: Option<&str>,
) -> Result<FileId, String> {
    let req = CreateRequest {
        requested_oplock_level: OplockLevel::None,
        impersonation_level: ImpersonationLevel::Impersonation,
        desired_access: FileAccessMask::new(
            FileAccessMask::FILE_WRITE_DATA
                | FileAccessMask::FILE_READ_ATTRIBUTES
                | FileAccessMask::SYNCHRONIZE,
        ),
        file_attributes: FILE_ATTRIBUTE_NORMAL,
        share_access: ShareAccess(0),
        create_disposition: CreateDisposition::FileOverwriteIf,
        create_options: FILE_NON_DIRECTORY_FILE,
        name: normalize_smb_path_for_copy(path),
        create_contexts: vec![],
    };
    let frame = await_smb_call(
        "创建目标文件",
        operation_id,
        conn.execute(Command::Create, &req, Some(tree.tree_id)),
    )
    .await?;
    if frame.header.status != NtStatus::SUCCESS {
        return Err(format!("{:?}", frame.header.status));
    }
    let mut cursor = ReadCursor::new(&frame.body);
    let resp = CreateResponse::unpack(&mut cursor).map_err(|error| error.to_string())?;
    Ok(resp.file_id)
}

async fn request_resume_key(
    conn: &mut smb2::client::connection::Connection,
    tree: &smb2::Tree,
    source_id: FileId,
    operation_id: Option<&str>,
) -> Result<[u8; 24], String> {
    let req = IoctlRequest {
        ctl_code: FSCTL_SRV_REQUEST_RESUME_KEY,
        file_id: source_id,
        max_input_response: 0,
        max_output_response: 32,
        flags: SMB2_0_IOCTL_IS_FSCTL,
        input_data: Vec::new(),
    };
    let frame = await_smb_call(
        "请求内部复制 key",
        operation_id,
        conn.execute(Command::Ioctl, &req, Some(tree.tree_id)),
    )
    .await?;
    if frame.header.status != NtStatus::SUCCESS {
        return Err(format!(
            "服务器不支持服务端复制 key: {:?}",
            frame.header.status
        ));
    }
    let mut cursor = ReadCursor::new(&frame.body);
    let resp = IoctlResponse::unpack(&mut cursor).map_err(|error| error.to_string())?;
    if resp.output_data.len() < 24 {
        return Err("服务器返回的复制 key 长度不足".to_string());
    }
    let mut key = [0_u8; 24];
    key.copy_from_slice(&resp.output_data[..24]);
    Ok(key)
}

async fn copy_chunks_server_side(
    conn: &mut smb2::client::connection::Connection,
    tree: &smb2::Tree,
    target_id: FileId,
    resume_key: &[u8; 24],
    source_size: u64,
    operation_id: Option<&str>,
) -> Result<(), String> {
    let mut offset = 0_u64;
    while offset < source_size {
        ensure_not_cancelled(operation_id)?;
        let length = (source_size - offset).min(COPYCHUNK_BYTES) as u32;
        let input_data = build_copychunk_request(resume_key, offset, offset, length);
        let req = IoctlRequest {
            ctl_code: FSCTL_SRV_COPYCHUNK,
            file_id: target_id,
            max_input_response: 0,
            max_output_response: 16,
            flags: SMB2_0_IOCTL_IS_FSCTL,
            input_data,
        };
        let frame = await_smb_call(
            "内部复制",
            operation_id,
            conn.execute(Command::Ioctl, &req, Some(tree.tree_id)),
        )
        .await?;
        if frame.header.status != NtStatus::SUCCESS {
            return Err(format!(
                "服务器拒绝内部复制 chunk: {:?}",
                frame.header.status
            ));
        }
        let mut cursor = ReadCursor::new(&frame.body);
        let resp = IoctlResponse::unpack(&mut cursor).map_err(|error| error.to_string())?;
        let bytes_written = parse_copychunk_total_bytes_written(&resp.output_data)?;
        if bytes_written < length {
            return Err(format!(
                "服务器内部复制未写满 chunk: {}/{}",
                bytes_written, length
            ));
        }
        offset += u64::from(length);
    }
    Ok(())
}

async fn flush_smb_handle(
    conn: &mut smb2::client::connection::Connection,
    tree: &smb2::Tree,
    file_id: FileId,
    operation_id: Option<&str>,
) -> Result<(), String> {
    let req = FlushRequest { file_id };
    let frame = await_smb_call(
        "刷新复制结果",
        operation_id,
        conn.execute(Command::Flush, &req, Some(tree.tree_id)),
    )
    .await?;
    if frame.header.status != NtStatus::SUCCESS {
        return Err(format!("刷新复制结果失败: {:?}", frame.header.status));
    }
    Ok(())
}

async fn close_smb_handle(
    conn: &mut smb2::client::connection::Connection,
    tree: &smb2::Tree,
    file_id: FileId,
    operation_id: Option<&str>,
) -> Result<(), String> {
    let req = CloseRequest { flags: 0, file_id };
    let frame = await_smb_call(
        "关闭复制句柄",
        operation_id,
        conn.execute(Command::Close, &req, Some(tree.tree_id)),
    )
    .await?;
    if frame.header.status != NtStatus::SUCCESS {
        return Err(format!("关闭复制句柄失败: {:?}", frame.header.status));
    }
    Ok(())
}

fn normalize_smb_path_for_copy(path: &str) -> String {
    path.replace('/', "\\").trim_start_matches('\\').to_string()
}

fn temporary_remote_path(path: &str) -> String {
    let parent = parent_remote_path(path);
    let name = remote_file_name(path);
    let temp_name = format!(".rynat-tmp-{}-{name}", Uuid::new_v4());
    if parent == "/" {
        format!("/{temp_name}")
    } else {
        format!("{parent}/{temp_name}")
    }
}

fn parent_remote_path(path: &str) -> String {
    let normalized = normalize_remote_result_path(path);
    if normalized == "/" {
        return "/".to_string();
    }
    match normalized.rsplit_once('/') {
        Some(("", _)) | None => "/".to_string(),
        Some((parent, _)) => parent.to_string(),
    }
}

fn remote_file_name(path: &str) -> String {
    normalize_remote_result_path(path)
        .rsplit('/')
        .next()
        .unwrap_or("")
        .to_string()
}

pub fn cancel_operation(operation_id: &str) {
    let id = operation_id.trim();
    if id.is_empty() {
        return;
    }
    cancelled_operations_guard().insert(id.to_string());
}

pub fn clear_operation(operation_id: Option<&str>) {
    if let Some(id) = operation_id {
        cancelled_operations_guard().remove(id);
    }
}

pub fn is_operation_cancelled(operation_id: Option<&str>) -> bool {
    operation_id.is_some_and(|id| cancelled_operations_guard().contains(id))
}

fn ensure_not_cancelled(operation_id: Option<&str>) -> Result<(), String> {
    if is_operation_cancelled(operation_id) {
        Err(CANCELLED_OPERATION_MESSAGE.to_string())
    } else {
        Ok(())
    }
}

async fn await_smb_call<T, E>(
    label: &str,
    operation_id: Option<&str>,
    future: impl Future<Output = std::result::Result<T, E>>,
) -> std::result::Result<T, String>
where
    E: Display,
{
    ensure_not_cancelled(operation_id)?;
    tokio::select! {
        result = future => {
            ensure_not_cancelled(operation_id)?;
            result.map_err(|error| error.to_string())
        }
        _ = wait_until_cancelled(operation_id), if operation_id.is_some() => {
            Err(CANCELLED_OPERATION_MESSAGE.to_string())
        }
        _ = tokio::time::sleep(SMB_CALL_TIMEOUT) => {
            Err(format!("{label}超时，请重试"))
        }
    }
}

async fn await_smb_optional_call<T, E>(
    label: &str,
    operation_id: Option<&str>,
    future: impl Future<Output = Option<std::result::Result<T, E>>>,
) -> std::result::Result<Option<std::result::Result<T, E>>, String> {
    ensure_not_cancelled(operation_id)?;
    tokio::select! {
        result = future => {
            ensure_not_cancelled(operation_id)?;
            Ok(result)
        }
        _ = wait_until_cancelled(operation_id), if operation_id.is_some() => {
            Err(CANCELLED_OPERATION_MESSAGE.to_string())
        }
        _ = tokio::time::sleep(SMB_CALL_TIMEOUT) => {
            Err(format!("{label}超时，请重试"))
        }
    }
}

async fn wait_until_cancelled(operation_id: Option<&str>) {
    while !is_operation_cancelled(operation_id) {
        tokio::time::sleep(Duration::from_millis(120)).await;
    }
}

fn cancelled_operations() -> &'static StdMutex<std::collections::HashSet<String>> {
    CANCELLED_OPERATIONS.get_or_init(|| StdMutex::new(std::collections::HashSet::new()))
}

fn cancelled_operations_guard() -> MutexGuard<'static, std::collections::HashSet<String>> {
    match cancelled_operations().lock() {
        Ok(guard) => guard,
        Err(poisoned) => poisoned.into_inner(),
    }
}

fn build_copychunk_request(
    resume_key: &[u8; 24],
    source_offset: u64,
    target_offset: u64,
    length: u32,
) -> Vec<u8> {
    let mut buffer = Vec::with_capacity(56);
    buffer.extend_from_slice(resume_key);
    buffer.extend_from_slice(&1_u32.to_le_bytes());
    buffer.extend_from_slice(&0_u32.to_le_bytes());
    buffer.extend_from_slice(&source_offset.to_le_bytes());
    buffer.extend_from_slice(&target_offset.to_le_bytes());
    buffer.extend_from_slice(&length.to_le_bytes());
    buffer.extend_from_slice(&0_u32.to_le_bytes());
    buffer
}

fn parse_copychunk_total_bytes_written(data: &[u8]) -> Result<u32, String> {
    if data.len() < 12 {
        return Err("服务器内部复制响应长度不足".to_string());
    }
    Ok(u32::from_le_bytes(
        data[4..8]
            .try_into()
            .map_err(|_| "服务器内部复制响应格式错误".to_string())?,
    ))
}

fn classify_connect_error(err: String) -> SmbOperationError {
    let lower = err.to_lowercase();
    if lower.contains("auth") || lower.contains("logon") || lower.contains("password") {
        SmbOperationError::new("账号或密码错误，请检查后重试", SmbErrorCode::Auth)
    } else if lower.contains("timeout") || lower.contains("timed out") || lower.contains("超时") {
        SmbOperationError::new(
            "连接超时，请检查服务器地址和网络",
            SmbErrorCode::Reconnectable,
        )
    } else if lower.contains("refused") || lower.contains("unreachable") {
        SmbOperationError::new(
            "无法连接到服务器，请检查地址是否正确、NAS 是否在线".to_string(),
            SmbErrorCode::Reconnectable,
        )
    } else if lower.contains("not found") || lower.contains("resolve") {
        SmbOperationError::new("找不到服务器，请检查服务器地址", SmbErrorCode::NotFound)
    } else if lower.contains("access") || lower.contains("denied") || lower.contains("permission") {
        SmbOperationError::new("没有权限访问，请联系管理员", SmbErrorCode::Permission)
    } else {
        SmbOperationError::new(format!("SMB 连接失败: {}", err), SmbErrorCode::Smb)
    }
}

fn filetime_to_unix(ft: smb2::pack::FileTime) -> Option<i64> {
    if ft.0 == 0 {
        return None;
    }
    ft.to_system_time()
        .map(|time| {
            time.duration_since(std::time::UNIX_EPOCH)
                .map(|duration| duration.as_secs() as i64)
                .unwrap_or(0)
        })
        .filter(|timestamp| *timestamp > 0)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn normalizes_common_smb_host_inputs() {
        assert_eq!(normalize_host("smb://192.168.1.2/"), "192.168.1.2");
        assert_eq!(normalize_host("\\\\nas.local\\"), "nas.local");
        assert_eq!(normalize_host("//nas.local/share"), "nas.local");
        assert_eq!(normalize_host("192.168.1.2:445"), "192.168.1.2");
    }

    #[test]
    fn normalizes_smb_endpoint_with_explicit_or_default_port() {
        assert_eq!(normalize_smb_endpoint("192.168.1.2"), "192.168.1.2:445");
        assert_eq!(
            normalize_smb_endpoint("smb://nas.local:1445/share"),
            "nas.local:1445"
        );
        assert_eq!(profile_host_from_endpoint("nas.local:445"), "nas.local");
        assert_eq!(
            profile_host_from_endpoint("nas.local:1445"),
            "nas.local:1445"
        );
    }

    #[test]
    fn maps_root_path_to_empty_smb_path() {
        assert_eq!(to_smb_dir_path("/"), "");
        assert_eq!(to_smb_dir_path(""), "");
        assert_eq!(to_smb_dir_path("/Photos/2026/"), "Photos/2026");
    }

    #[test]
    fn creates_temp_path_next_to_target() {
        let path = temporary_remote_path("/Docs/report.pdf");

        assert!(path.starts_with("/Docs/.rynat-tmp-"));
        assert!(path.ends_with("-report.pdf"));
    }

    #[test]
    fn extracts_parent_and_file_name_from_remote_path() {
        assert_eq!(parent_remote_path("/Docs/report.pdf"), "/Docs");
        assert_eq!(parent_remote_path("report.pdf"), "/");
        assert_eq!(remote_file_name("/Docs/report.pdf"), "report.pdf");
    }

    #[test]
    fn tracks_cancelled_operations() {
        let operation_id = format!("test-{}", Uuid::new_v4());

        assert!(!is_operation_cancelled(Some(&operation_id)));
        cancel_operation(&operation_id);
        assert!(is_operation_cancelled(Some(&operation_id)));
        assert_eq!(
            ensure_not_cancelled(Some(&operation_id)).unwrap_err(),
            CANCELLED_OPERATION_MESSAGE
        );
        clear_operation(Some(&operation_id));
        assert!(!is_operation_cancelled(Some(&operation_id)));
    }
}
