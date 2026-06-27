use std::env;
use std::process;

use rynat_windows_shell_support::{ContextRequest, DEFAULT_CONTEXT_IPC_PORT, send_context_request};

fn main() {
    let helper_args = HelperArgs::parse(env::args().skip(1));
    let request = match ContextRequest::parse_args(helper_args.context_args) {
        Ok(request) => request,
        Err(error) => {
            eprintln!("{error}");
            eprintln!(
                r#"Usage: rynat-windows-context-helper [--print-only] copy-link "\\host\share\path""#
            );
            process::exit(2);
        }
    };

    if helper_args.print_only {
        match request.to_activation_payload() {
            Ok(payload) => {
                println!("{payload}");
                return;
            }
            Err(error) => {
                eprintln!("failed to serialize context request: {error}");
                process::exit(1);
            }
        }
    }

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

#[derive(Debug, Clone, PartialEq, Eq)]
struct HelperArgs {
    print_only: bool,
    context_args: Vec<String>,
}

impl HelperArgs {
    fn parse<I, S>(args: I) -> Self
    where
        I: IntoIterator<Item = S>,
        S: Into<String>,
    {
        let mut print_only = false;
        let mut context_args = Vec::new();

        for arg in args.into_iter().map(Into::into) {
            if arg == "--print-only" {
                print_only = true;
            } else {
                context_args.push(arg);
            }
        }

        Self {
            print_only,
            context_args,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_print_only_without_passing_flag_to_context_parser() {
        let args = HelperArgs::parse([
            "--print-only",
            "copy-link",
            r"\\nas.local\Media\demo.mp4",
            "--kind",
            "file",
        ]);

        assert!(args.print_only);
        assert_eq!(
            args.context_args,
            vec![
                "copy-link".to_string(),
                r"\\nas.local\Media\demo.mp4".to_string(),
                "--kind".to_string(),
                "file".to_string()
            ]
        );
    }

    #[test]
    fn keeps_runtime_args_unchanged_without_print_only() {
        let args = HelperArgs::parse(["copy-link", r"\\nas.local\Media"]);

        assert!(!args.print_only);
        assert_eq!(
            args.context_args,
            vec!["copy-link".to_string(), r"\\nas.local\Media".to_string()]
        );
    }
}
