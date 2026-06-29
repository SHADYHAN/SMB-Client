use rynat_core::{LinkKind, QuickLink};
use rynat_windows_shell_support::{
    ContextAction, ContextMenuRegistration, ContextRequest, ContextResponse,
    DEFAULT_CONTEXT_IPC_PORT, DEFAULT_LOCAL_REDIRECT_PORT, ExplorerTarget, ProtocolRegistration,
    SmbSessionConnectRequest, UncPath, explorer_target_from_link, start_context_ipc_server,
    start_local_redirect_server, windows_context_menu_reg_file, windows_protocol_reg_file,
};
#[cfg(windows)]
use rynat_windows_shell_support::SmbSessionConnector;
use serde::Serialize;
use std::sync::{Mutex, OnceLock};

static RUNTIME_STATUS: OnceLock<Mutex<RuntimeStatus>> = OnceLock::new();

#[derive(Debug, Clone)]
struct RuntimeStatus {
    context_ipc: ServiceStatus,
    local_redirect: ServiceStatus,
    last_activation: String,
}

#[derive(Debug, Clone)]
struct ServiceStatus {
    running: bool,
    message: String,
}

impl RuntimeStatus {
    fn starting() -> Self {
        Self {
            context_ipc: ServiceStatus::starting("右键 IPC 服务尚未启动"),
            local_redirect: ServiceStatus::starting("短链唤醒服务尚未启动"),
            last_activation: "尚未收到链接唤醒请求".to_string(),
        }
    }
}

impl ServiceStatus {
    fn starting(message: impl Into<String>) -> Self {
        Self {
            running: false,
            message: message.into(),
        }
    }

    fn running(message: impl Into<String>) -> Self {
        Self {
            running: true,
            message: message.into(),
        }
    }

    fn failed(message: impl Into<String>) -> Self {
        Self {
            running: false,
            message: message.into(),
        }
    }
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct ShellState {
    connected: bool,
    server_name: String,
    server_host: String,
    status: String,
    context_ipc_running: bool,
    context_ipc_status: String,
    local_redirect_running: bool,
    local_redirect_status: String,
    last_activation: String,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct RuntimeStatusDto {
    context_ipc_running: bool,
    context_ipc_status: String,
    local_redirect_running: bool,
    local_redirect_status: String,
    last_activation: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
struct ExplorerOpenTarget {
    host: String,
    share: Option<String>,
    open_path: String,
}

#[tauri::command]
fn get_bootstrap_state() -> ShellState {
    let runtime = runtime_snapshot();
    let runtime = RuntimeStatusDto::from(runtime);
    ShellState {
        connected: false,
        server_name: "RYNAT 文件共享".to_string(),
        server_host: String::new(),
        status: "未连接".to_string(),
        context_ipc_running: runtime.context_ipc_running,
        context_ipc_status: runtime.context_ipc_status,
        local_redirect_running: runtime.local_redirect_running,
        local_redirect_status: runtime.local_redirect_status,
        last_activation: runtime.last_activation,
    }
}

#[tauri::command]
fn get_runtime_status() -> RuntimeStatusDto {
    RuntimeStatusDto::from(runtime_snapshot())
}

#[tauri::command]
fn connect_profile(host: String, username: String, password: String) -> Result<(), String> {
    let target = parse_explorer_open_target(&host, None)?;
    let request = SmbSessionConnectRequest {
        host: target.host,
        share: target.share,
        username: (!username.trim().is_empty()).then_some(username),
        password: (!password.is_empty()).then_some(password),
    };

    #[cfg(windows)]
    {
        let connector = rynat_windows_shell_support::smb_session::windows::WindowsSmbSessionConnector;
        connector.connect(&request).map_err(|error| error.to_string())?;
    }

    #[cfg(not(windows))]
    {
        let _ = request;
    }

    Ok(())
}

#[tauri::command]
fn open_explorer(host: String, share: Option<String>) -> Result<String, String> {
    let target = parse_explorer_open_target(&host, share.as_deref())?;

    open_path_with_explorer(&target.open_path)?;

    Ok(target.open_path)
}

#[tauri::command]
fn preview_explorer_path(host: String, share: Option<String>) -> Result<ExplorerOpenTarget, String> {
    parse_explorer_open_target(&host, share.as_deref())
}

fn parse_explorer_open_target(
    input: &str,
    share: Option<&str>,
) -> Result<ExplorerOpenTarget, String> {
    let input = normalize_windows_path_input(input);
    if input.is_empty() {
        return Err("服务器地址不能为空".to_string());
    }

    let (host, parsed_share, open_path) = if let Some(unc_tail) = input.strip_prefix(r"\\") {
        let mut parts = unc_tail.split('\\').filter(|part| !part.trim().is_empty());
        let host = parts
            .next()
            .ok_or_else(|| "UNC 路径缺少服务器地址".to_string())?
            .trim()
            .to_string();
        let parsed_share = parts.next().map(|value| value.trim().to_string());
        (host, parsed_share, input)
    } else if let Some((host, rest)) = input.split_once('\\') {
        let host = host.trim().to_string();
        let parsed_share = rest
            .split('\\')
            .next()
            .map(str::trim)
            .filter(|value| !value.is_empty())
            .map(str::to_string);
        (host, parsed_share, format!(r"\\{input}"))
    } else {
        let explicit_share = share
            .map(str::trim)
            .filter(|value| !value.is_empty())
            .map(str::to_string);
        let open_path = match explicit_share.as_deref() {
            Some(share) => rynat_windows_shell_support::explorer::unc_path(&input, share, "/"),
            None => format!(r"\\{}", input.trim()),
        };
        (input.trim().to_string(), explicit_share, open_path)
    };

    let share = match share.map(str::trim).filter(|value| !value.is_empty()) {
        Some(share) => Some(share.to_string()),
        None => parsed_share,
    };

    let open_path = match share.as_deref() {
        Some(share) if !share.trim().is_empty() => {
            rynat_windows_shell_support::explorer::unc_path(&host, share, "/")
        }
        _ => open_path,
    };

    Ok(ExplorerOpenTarget {
        host,
        share,
        open_path,
    })
}

fn normalize_windows_path_input(value: &str) -> String {
    let mut value = value.trim().trim_matches('"').replace('/', "\\");
    if let Some(rest) = value.strip_prefix("smb:\\\\") {
        value = format!(r"\\{rest}");
    }

    while value.ends_with('\\') && value.len() > 2 {
        value.pop();
    }

    value
}

fn open_path_with_explorer(path: &str) -> Result<(), String> {
    #[cfg(windows)]
    {
        return std::process::Command::new("explorer.exe")
            .arg(path)
            .spawn()
            .map(|_| ())
            .map_err(|error| format!("failed to start explorer.exe for {path}: {error}"));
    }

    #[cfg(not(windows))]
    {
        let _ = path;
        Ok(())
    }
}

fn open_explorer_target(target: &ExplorerTarget) -> Result<(), String> {
    #[cfg(windows)]
    {
        let mut command = std::process::Command::new("explorer.exe");
        if let Some(argument) = target.explorer_select_argument() {
            command.arg(argument);
        } else {
            command.arg(&target.open_path);
        }
        return command
            .spawn()
            .map(|_| ())
            .map_err(|error| format!("failed to start explorer.exe for {}: {error}", target.open_path));
    }

    #[cfg(not(windows))]
    {
        let _ = target;
        Ok(())
    }
}

#[tauri::command]
fn copy_link_for_unc_path(path: String, kind: String) -> Result<String, String> {
    let kind = parse_link_kind(&kind);
    build_link_for_unc_path(&path, kind)
}

fn build_link_for_unc_path(path: &str, kind: LinkKind) -> Result<String, String> {
    let unc = UncPath::parse(&path, kind).map_err(|error| error.to_string())?;
    let target = unc.to_link_target();
    let link = QuickLink::create(target).map_err(|error| error.to_string())?;
    Ok(link.http_url)
}

#[tauri::command]
fn explorer_target_for_link(raw_link: String) -> Result<ExplorerTargetDto, String> {
    let activation = explorer_target_from_link(&raw_link).map_err(|error| error.to_string())?;
    Ok(ExplorerTargetDto::from(activation.explorer))
}

#[tauri::command]
fn registration_preview(executable_path: String, helper_path: String) -> Result<RegistrationPreview, String> {
    let protocol = windows_protocol_reg_file(&ProtocolRegistration { executable_path })
        .map_err(|error| error.to_string())?;
    let context_menu = windows_context_menu_reg_file(&ContextMenuRegistration {
        helper_path,
        menu_text: "复制 RYNAT 分享链接".to_string(),
    })
    .map_err(|error| error.to_string())?;

    Ok(RegistrationPreview {
        protocol,
        context_menu,
    })
}

#[derive(Debug, Serialize)]
struct ExplorerTargetDto {
    open_path: String,
    selected_path: Option<String>,
}

#[derive(Debug, Serialize)]
struct RegistrationPreview {
    protocol: String,
    context_menu: String,
}

impl From<ExplorerTarget> for ExplorerTargetDto {
    fn from(value: ExplorerTarget) -> Self {
        Self {
            open_path: value.open_path,
            selected_path: value.selected_path,
        }
    }
}

impl From<RuntimeStatus> for RuntimeStatusDto {
    fn from(value: RuntimeStatus) -> Self {
        Self {
            context_ipc_running: value.context_ipc.running,
            context_ipc_status: value.context_ipc.message,
            local_redirect_running: value.local_redirect.running,
            local_redirect_status: value.local_redirect.message,
            last_activation: value.last_activation,
        }
    }
}

pub fn run() {
    initialize_runtime_status();
    let _context_ipc_thread =
        match start_context_ipc_server(DEFAULT_CONTEXT_IPC_PORT, handle_context_request) {
            Ok(handle) => {
                set_context_ipc_status(ServiceStatus::running(format!(
                    "右键 IPC 正在监听 127.0.0.1:{DEFAULT_CONTEXT_IPC_PORT}"
                )));
                Some(handle)
            }
            Err(error) => {
                set_context_ipc_status(ServiceStatus::failed(error.to_string()));
                None
            }
        };
    let _local_redirect_thread =
        match start_local_redirect_server(DEFAULT_LOCAL_REDIRECT_PORT, handle_link_activation) {
            Ok(handle) => {
                set_local_redirect_status(ServiceStatus::running(format!(
                    "短链唤醒正在监听 127.0.0.1:{DEFAULT_LOCAL_REDIRECT_PORT}"
                )));
                Some(handle)
            }
            Err(error) => {
                set_local_redirect_status(ServiceStatus::failed(error.to_string()));
                None
            }
        };

    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![
            get_bootstrap_state,
            get_runtime_status,
            connect_profile,
            open_explorer,
            preview_explorer_path,
            copy_link_for_unc_path,
            explorer_target_for_link,
            registration_preview
        ])
        .run(tauri::generate_context!())
        .expect("error while running RYNAT Windows shell");
}

fn handle_link_activation(raw_link: String) -> Result<(), String> {
    let activation = match explorer_target_from_link(&raw_link) {
        Ok(activation) => activation,
        Err(error) => {
            let message = format!("唤醒失败：{error}");
            set_last_activation(message.clone());
            return Err(message);
        }
    };
    if let Err(error) = open_explorer_target(&activation.explorer) {
        let message = format!("打开 Explorer 失败：{error}");
        set_last_activation(message.clone());
        return Err(message);
    }
    let selected = activation
        .explorer
        .selected_path
        .as_deref()
        .unwrap_or("未选择文件");
    set_last_activation(format!(
        "已打开：{}；选中：{}",
        activation.explorer.open_path, selected
    ));
    Ok(())
}

fn initialize_runtime_status() {
    let _ = RUNTIME_STATUS.set(Mutex::new(RuntimeStatus::starting()));
}

fn runtime_snapshot() -> RuntimeStatus {
    let status = RUNTIME_STATUS.get_or_init(|| Mutex::new(RuntimeStatus::starting()));
    status
        .lock()
        .map(|guard| guard.clone())
        .unwrap_or_else(|_| RuntimeStatus::starting())
}

fn set_context_ipc_status(status: ServiceStatus) {
    update_runtime_status(|runtime| runtime.context_ipc = status);
}

fn set_local_redirect_status(status: ServiceStatus) {
    update_runtime_status(|runtime| runtime.local_redirect = status);
}

fn set_last_activation(message: String) {
    update_runtime_status(|runtime| runtime.last_activation = message);
}

fn update_runtime_status(update: impl FnOnce(&mut RuntimeStatus)) {
    let status = RUNTIME_STATUS.get_or_init(|| Mutex::new(RuntimeStatus::starting()));
    if let Ok(mut runtime) = status.lock() {
        update(&mut runtime);
    }
}

fn handle_context_request(request: ContextRequest) -> ContextResponse {
    match request.action {
        ContextAction::CopyLink => {
            let kind = request.kind.unwrap_or(LinkKind::Unknown);
            match build_link_for_unc_path(&request.path, kind) {
                Ok(http_url) => ContextResponse::copied(http_url),
                Err(error) => ContextResponse::failed(error),
            }
        }
    }
}

fn parse_link_kind(kind: &str) -> LinkKind {
    match kind {
        "file" => LinkKind::File,
        "dir" | "directory" => LinkKind::Directory,
        _ => LinkKind::Unknown,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn context_request_builds_share_link() {
        let response = handle_context_request(ContextRequest::copy_link_with_kind(
            r"\\192.168.102.136\共享资料\123",
            Some(LinkKind::Directory),
        )
        .unwrap());

        assert!(response.ok);
        assert!(response.http_url.unwrap().starts_with("http://127.0.0.1:19527/s/"));
    }

    #[test]
    fn http_link_activation_calculates_explorer_target() {
        let http_url = build_link_for_unc_path(
            r"\\192.168.102.136\共享资料\123\demo.txt",
            LinkKind::File,
        )
        .unwrap();
        let activation = explorer_target_from_link(&http_url).unwrap();

        assert_eq!(activation.explorer.open_path, r"\\192.168.102.136\共享资料\123");
    }

    #[test]
    fn test_share_directory_link_opens_requested_unc_directory() {
        let http_url =
            build_link_for_unc_path(r"\\192.168.102.136\临时文件夹\123", LinkKind::Directory)
                .unwrap();
        let activation = explorer_target_from_link(&http_url).unwrap();

        assert_eq!(
            activation.explorer.open_path,
            r"\\192.168.102.136\临时文件夹\123"
        );
        assert_eq!(activation.explorer.selected_path, None);
    }

    #[test]
    fn login_open_target_defaults_to_server_unc_root() {
        assert_eq!(
            parse_explorer_open_target(" 192.168.102.136 ", None)
                .unwrap()
                .open_path,
            r"\\192.168.102.136"
        );
    }

    #[test]
    fn login_open_target_can_include_share() {
        assert_eq!(
            parse_explorer_open_target("192.168.102.136", Some("共享资料"))
                .unwrap()
                .open_path,
            r"\\192.168.102.136\共享资料"
        );
    }

    #[test]
    fn login_open_target_keeps_unc_input() {
        assert_eq!(
            parse_explorer_open_target(r"\\192.168.102.136\共享资料", None)
                .unwrap()
                .open_path,
            r"\\192.168.102.136\共享资料"
        );
    }

    #[test]
    fn login_open_target_accepts_smb_url_style_input() {
        assert_eq!(
            parse_explorer_open_target("smb://192.168.102.136/共享资料", None)
                .unwrap()
                .open_path,
            r"\\192.168.102.136\共享资料"
        );
    }

    #[test]
    fn login_open_target_extracts_host_and_share_from_unc_input() {
        let target = parse_explorer_open_target(r"\\192.168.102.136\共享资料", None).unwrap();

        assert_eq!(target.host, "192.168.102.136");
        assert_eq!(target.share, Some("共享资料".to_string()));
        assert_eq!(target.open_path, r"\\192.168.102.136\共享资料");
    }
}
