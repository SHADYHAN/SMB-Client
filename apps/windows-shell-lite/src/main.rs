#![cfg_attr(windows, windows_subsystem = "windows")]

use std::thread;
use std::time::Duration;

use rynat_core::LinkKind;
use rynat_windows_shell_support::{
    ContextAction, ContextRequest, ContextResponse, DEFAULT_CONTEXT_IPC_PORT,
    DEFAULT_LOCAL_REDIRECT_PORT, UncPath, explorer_target_from_link, start_context_ipc_server,
    start_local_redirect_server,
};

#[cfg(windows)]
const DEFAULT_SERVER_ROOT: &str = r"\\192.168.102.136";

fn main() {
    let _context_ipc = start_context_ipc_server(DEFAULT_CONTEXT_IPC_PORT, handle_context_request);
    let _local_redirect =
        start_local_redirect_server(DEFAULT_LOCAL_REDIRECT_PORT, handle_link_activation);

    #[cfg(windows)]
    let _ = open_explorer(DEFAULT_SERVER_ROOT);

    loop {
        thread::sleep(Duration::from_secs(3600));
    }
}

fn handle_link_activation(raw_link: String) -> Result<(), String> {
    if let Ok(activation) = explorer_target_from_link(&raw_link) {
        #[cfg(windows)]
        {
            if let Some(argument) = activation.explorer.explorer_select_argument() {
                open_explorer_argument(&argument).map_err(|error| error.to_string())?;
            } else {
                open_explorer(&activation.explorer.open_path).map_err(|error| error.to_string())?;
            }
        }

        #[cfg(not(windows))]
        {
            let _ = activation;
        }
    }

    Ok(())
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

fn build_link_for_unc_path(path: &str, kind: LinkKind) -> Result<String, String> {
    let unc = UncPath::parse(path, kind).map_err(|error| error.to_string())?;
    let target = unc.to_link_target();
    let link = rynat_core::QuickLink::create(target).map_err(|error| error.to_string())?;
    Ok(link.http_url)
}

#[cfg(windows)]
fn open_explorer(path: &str) -> std::io::Result<()> {
    std::process::Command::new("explorer.exe")
        .arg(path)
        .spawn()
        .map(|_| ())
}

#[cfg(windows)]
fn open_explorer_argument(argument: &str) -> std::io::Result<()> {
    std::process::Command::new("explorer.exe")
        .arg(argument)
        .spawn()
        .map(|_| ())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn context_request_builds_local_short_link() {
        let response = handle_context_request(
            ContextRequest::copy_link_with_kind(
                r"\\192.168.102.136\临时文件夹\123",
                Some(LinkKind::Directory),
            )
            .unwrap(),
        );

        assert!(response.ok);
        assert!(
            response
                .http_url
                .unwrap()
                .starts_with("http://127.0.0.1:19527/s/")
        );
    }
}
