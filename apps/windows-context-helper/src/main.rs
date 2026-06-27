use std::env;
use std::process;

use rynat_windows_shell_support::{ContextRequest, DEFAULT_CONTEXT_IPC_PORT, send_context_request};

fn main() {
    let request = match ContextRequest::parse_args(env::args().skip(1)) {
        Ok(request) => request,
        Err(error) => {
            eprintln!("{error}");
            eprintln!(r#"Usage: rynat-windows-context-helper copy-link "\\host\share\path""#);
            process::exit(2);
        }
    };

    match send_context_request(&request, DEFAULT_CONTEXT_IPC_PORT) {
        Ok(response) if response.ok => {
            if let Some(message) = response.message {
                println!("{message}");
            }
            if let Some(http_url) = response.http_url {
                println!("{http_url}");
            }
        }
        Ok(response) => {
            eprintln!(
                "{}",
                response
                    .message
                    .unwrap_or_else(|| "RYNAT context request failed".to_string())
            );
            process::exit(1);
        }
        Err(error) => match request.to_activation_payload() {
            Ok(payload) => {
                eprintln!("RYNAT main app is not reachable: {error}");
                println!("{payload}");
                process::exit(3);
            }
            Err(error) => {
                eprintln!("failed to serialize context request: {error}");
                process::exit(1);
            }
        },
    }
}
