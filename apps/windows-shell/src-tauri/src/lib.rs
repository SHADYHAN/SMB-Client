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

#[derive(Debug, Serialize)]
struct ShellState {
    connected: bool,
    server_name: String,
    server_host: String,
    status: String,
}

#[tauri::command]
fn get_bootstrap_state() -> ShellState {
    ShellState {
        connected: false,
        server_name: "RYNAT 文件共享".to_string(),
        server_host: String::new(),
        status: "未连接".to_string(),
    }
}

#[tauri::command]
fn connect_profile(host: String, username: String, password: String) -> Result<(), String> {
    let request = SmbSessionConnectRequest {
        host,
        share: None,
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
    let open_path = explorer_open_path(&host, share.as_deref());

    #[cfg(windows)]
    {
        open_path_with_shell(&open_path)?;
    }

    Ok(open_path)
}

#[tauri::command]
fn preview_explorer_path(host: String, share: Option<String>) -> Result<String, String> {
    Ok(explorer_open_path(&host, share.as_deref()))
}

fn explorer_open_path(host: &str, share: Option<&str>) -> String {
    let host = normalize_windows_path_input(host);

    if host.starts_with(r"\\") && share.map(str::trim).filter(|value| !value.is_empty()).is_none()
    {
        return host;
    }

    match share {
        Some(share) if !share.trim().is_empty() => {
            rynat_windows_shell_support::explorer::unc_path(&host, share, "/")
        }
        _ if host.contains('\\') => format!(r"\\{host}"),
        _ => format!(r"\\{}", host.trim()),
    }
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

#[cfg(windows)]
fn open_path_with_shell(path: &str) -> Result<(), String> {
    use std::ffi::OsStr;
    use std::iter;
    use std::os::windows::ffi::OsStrExt;
    use std::ptr;

    const SW_SHOWNORMAL: i32 = 1;

    #[link(name = "Shell32")]
    unsafe extern "system" {
        fn ShellExecuteW(
            hwnd: *mut core::ffi::c_void,
            lp_operation: *const u16,
            lp_file: *const u16,
            lp_parameters: *const u16,
            lp_directory: *const u16,
            n_show_cmd: i32,
        ) -> *mut core::ffi::c_void;
    }

    fn wide_null(value: &str) -> Vec<u16> {
        OsStr::new(value)
            .encode_wide()
            .chain(iter::once(0))
            .collect()
    }

    let operation = wide_null("open");
    let file = wide_null(path);
    let result = unsafe {
        ShellExecuteW(
            ptr::null_mut(),
            operation.as_ptr(),
            file.as_ptr(),
            ptr::null::<u16>(),
            ptr::null::<u16>(),
            SW_SHOWNORMAL,
        ) as isize
    };

    if result > 32 {
        Ok(())
    } else {
        Err(format!("ShellExecuteW failed with code {result} for {path}"))
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

pub fn run() {
    let _context_ipc = start_context_ipc_server(DEFAULT_CONTEXT_IPC_PORT, handle_context_request);
    let _local_redirect =
        start_local_redirect_server(DEFAULT_LOCAL_REDIRECT_PORT, handle_deep_link_activation);

    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![
            get_bootstrap_state,
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

fn handle_deep_link_activation(deep_link: String) {
    if let Ok(activation) = explorer_target_from_link(&deep_link) {
        #[cfg(windows)]
        {
            let mut command = std::process::Command::new("explorer.exe");
            if let Some(argument) = activation.explorer.explorer_select_argument() {
                command.arg(argument);
            } else {
                command.arg(&activation.explorer.open_path);
            }
            let _ = command.spawn();
        }

        #[cfg(not(windows))]
        {
            let _ = activation;
        }
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
    fn deep_link_activation_calculates_explorer_target() {
        let http_url = build_link_for_unc_path(
            r"\\192.168.102.136\共享资料\123\demo.txt",
            LinkKind::File,
        )
        .unwrap();
        let activation = explorer_target_from_link(&http_url).unwrap();

        assert_eq!(activation.explorer.open_path, r"\\192.168.102.136\共享资料\123");
    }

    #[test]
    fn login_open_target_defaults_to_server_unc_root() {
        assert_eq!(explorer_open_path(" 192.168.102.136 ", None), r"\\192.168.102.136");
    }

    #[test]
    fn login_open_target_can_include_share() {
        assert_eq!(
            explorer_open_path("192.168.102.136", Some("共享资料")),
            r"\\192.168.102.136\共享资料"
        );
    }

    #[test]
    fn login_open_target_keeps_unc_input() {
        assert_eq!(
            explorer_open_path(r"\\192.168.102.136\共享资料", None),
            r"\\192.168.102.136\共享资料"
        );
    }

    #[test]
    fn login_open_target_accepts_smb_url_style_input() {
        assert_eq!(
            explorer_open_path("smb://192.168.102.136/共享资料", None),
            r"\\192.168.102.136\共享资料"
        );
    }
}
