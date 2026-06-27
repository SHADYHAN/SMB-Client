use std::io::{Read, Write};
use std::net::{TcpListener, TcpStream};
use std::sync::Arc;
use std::thread::{self, JoinHandle};
use std::time::Duration;

use rynat_core::redirect_page::{RedirectPageOptions, build_local_activation_close_page_for_url};
use thiserror::Error;

use crate::activation::deep_link_from_local_request_line;

pub const DEFAULT_LOCAL_REDIRECT_PORT: u16 = rynat_core::link::DEFAULT_REDIRECT_PORT;
const MAX_REQUEST_BYTES: usize = 8192;

#[derive(Debug, Error)]
pub enum LocalRedirectError {
    #[error("local redirect unavailable: {0}")]
    Io(#[from] std::io::Error),
}

pub fn start_local_redirect_server<F>(
    port: u16,
    handler: F,
) -> Result<JoinHandle<()>, LocalRedirectError>
where
    F: Fn(String) + Send + Sync + 'static,
{
    let listener = TcpListener::bind(("127.0.0.1", port))?;
    let handler = Arc::new(handler);

    Ok(thread::spawn(move || {
        for stream in listener.incoming() {
            let Ok(stream) = stream else {
                continue;
            };
            let handler = Arc::clone(&handler);
            thread::spawn(move || {
                let _ = handle_stream(stream, handler.as_ref());
            });
        }
    }))
}

fn handle_stream<F>(mut stream: TcpStream, handler: &F) -> Result<(), LocalRedirectError>
where
    F: Fn(String),
{
    stream.set_read_timeout(Some(Duration::from_secs(3)))?;
    stream.set_write_timeout(Some(Duration::from_secs(3)))?;

    let raw = read_http_request(&mut stream)?;

    let response =
        match request_line(&raw).and_then(|line| deep_link_from_local_request_line(line).ok()) {
            Some(deep_link) => {
                handler(deep_link.clone());
                let body = build_local_activation_close_page_for_url(
                    &deep_link,
                    &RedirectPageOptions::default(),
                )
                .unwrap_or_else(|_| fallback_close_page());
                http_response("200 OK", "text/html; charset=utf-8", &body)
            }
            None => http_response("404 Not Found", "text/plain; charset=utf-8", "Not Found"),
        };

    stream.write_all(response.as_bytes())?;
    Ok(())
}

fn read_http_request(stream: &mut TcpStream) -> Result<String, LocalRedirectError> {
    let mut raw = Vec::new();
    let mut buffer = [0; 1024];
    while raw.len() < MAX_REQUEST_BYTES {
        let bytes_read = stream.read(&mut buffer)?;
        if bytes_read == 0 {
            break;
        }

        raw.extend_from_slice(&buffer[..bytes_read]);
        if raw.windows(4).any(|window| window == b"\r\n\r\n") {
            break;
        }
    }

    Ok(String::from_utf8_lossy(&raw).into_owned())
}

fn request_line(raw: &str) -> Option<&str> {
    raw.lines().next()
}

fn http_response(status: &str, content_type: &str, body: &str) -> String {
    format!(
        "HTTP/1.1 {status}\r\nContent-Type: {content_type}\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{body}",
        body.as_bytes().len()
    )
}

fn fallback_close_page() -> String {
    "<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><title>RYNAT</title></head><body>已打开 RYNAT，可以关闭此标签页。</body></html>".to_string()
}

#[cfg(test)]
mod tests {
    use super::*;
    use rynat_core::{LinkKind, QuickLinkTarget, build_http_link};

    #[test]
    fn local_redirect_request_line_converts_short_link() {
        let target = QuickLinkTarget::new("nas.local", "Media", "/demo.txt", None, LinkKind::File);
        let http_url = build_http_link(DEFAULT_LOCAL_REDIRECT_PORT, &target).unwrap();
        let path = http_url
            .strip_prefix(&format!("http://127.0.0.1:{DEFAULT_LOCAL_REDIRECT_PORT}"))
            .unwrap();
        let line = format!("GET {path} HTTP/1.1");
        let deep_link = deep_link_from_local_request_line(&line).unwrap();

        assert!(deep_link.starts_with("rynat://s/"));
    }

    #[test]
    fn request_line_is_available_before_connection_close() {
        let raw = "GET /s/demo HTTP/1.1\r\nHost: 127.0.0.1:19527\r\nConnection: keep-alive\r\n\r\n";

        assert_eq!(request_line(raw), Some("GET /s/demo HTTP/1.1"));
        assert_eq!(
            deep_link_from_local_request_line(request_line(raw).unwrap()).unwrap(),
            "rynat://s/demo"
        );
    }
}
