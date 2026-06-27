use std::io::{Read, Write};
use std::net::{Shutdown, SocketAddr, TcpListener, TcpStream};
use std::sync::Arc;
use std::thread::{self, JoinHandle};
use std::time::Duration;

use thiserror::Error;

use crate::{ContextRequest, ContextResponse};

pub const DEFAULT_CONTEXT_IPC_PORT: u16 = 19528;
const CONTEXT_PATH: &str = "/context";
const MAX_REQUEST_BYTES: usize = 64 * 1024;

#[derive(Debug, Error)]
pub enum ContextIpcError {
    #[error("context IPC unavailable: {0}")]
    Io(#[from] std::io::Error),
    #[error("invalid context IPC request: {0}")]
    InvalidRequest(String),
    #[error("invalid context IPC response: {0}")]
    InvalidResponse(String),
    #[error("json error: {0}")]
    Json(#[from] serde_json::Error),
}

pub fn send_context_request(
    request: &ContextRequest,
    port: u16,
) -> Result<ContextResponse, ContextIpcError> {
    let address = SocketAddr::from(([127, 0, 0, 1], port));
    let mut stream = TcpStream::connect_timeout(&address, Duration::from_millis(700))?;
    stream.set_read_timeout(Some(Duration::from_secs(3)))?;
    stream.set_write_timeout(Some(Duration::from_secs(3)))?;
    stream.write_all(build_context_http_request(request)?.as_bytes())?;
    let _ = stream.shutdown(Shutdown::Write);

    let mut raw = String::new();
    stream.read_to_string(&mut raw)?;
    parse_context_http_response(&raw)
}

pub fn start_context_ipc_server<F>(port: u16, handler: F) -> Result<JoinHandle<()>, ContextIpcError>
where
    F: Fn(ContextRequest) -> ContextResponse + Send + Sync + 'static,
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

pub fn build_context_http_request(request: &ContextRequest) -> Result<String, ContextIpcError> {
    let body = request.to_activation_payload()?;
    Ok(format!(
        "POST {CONTEXT_PATH} HTTP/1.1\r\nHost: 127.0.0.1:{DEFAULT_CONTEXT_IPC_PORT}\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{body}",
        body.len()
    ))
}

pub fn parse_context_http_request(raw: &str) -> Result<ContextRequest, ContextIpcError> {
    let (headers, body) = raw
        .split_once("\r\n\r\n")
        .ok_or_else(|| ContextIpcError::InvalidRequest("missing header terminator".to_string()))?;
    let mut lines = headers.lines();
    let request_line = lines
        .next()
        .ok_or_else(|| ContextIpcError::InvalidRequest("missing request line".to_string()))?;
    let parts = request_line.split_whitespace().collect::<Vec<_>>();
    if parts.len() != 3 || parts[0] != "POST" || parts[1] != CONTEXT_PATH {
        return Err(ContextIpcError::InvalidRequest(format!(
            "unsupported request line '{request_line}'"
        )));
    }

    serde_json::from_str(body).map_err(ContextIpcError::from)
}

pub fn parse_context_http_response(raw: &str) -> Result<ContextResponse, ContextIpcError> {
    let (headers, body) = raw
        .split_once("\r\n\r\n")
        .ok_or_else(|| ContextIpcError::InvalidResponse("missing header terminator".to_string()))?;
    let status_line = headers
        .lines()
        .next()
        .ok_or_else(|| ContextIpcError::InvalidResponse("missing status line".to_string()))?;
    if !status_line.starts_with("HTTP/1.1 2") {
        return Err(ContextIpcError::InvalidResponse(status_line.to_string()));
    }

    serde_json::from_str(body).map_err(ContextIpcError::from)
}

fn handle_stream<F>(mut stream: TcpStream, handler: &F) -> Result<(), ContextIpcError>
where
    F: Fn(ContextRequest) -> ContextResponse,
{
    stream.set_read_timeout(Some(Duration::from_secs(3)))?;
    stream.set_write_timeout(Some(Duration::from_secs(3)))?;
    let mut raw = String::new();
    Read::by_ref(&mut stream)
        .take(MAX_REQUEST_BYTES as u64)
        .read_to_string(&mut raw)?;
    let response = match parse_context_http_request(&raw) {
        Ok(request) => handler(request),
        Err(error) => ContextResponse::failed(error.to_string()),
    };
    let status = if response.ok {
        "200 OK"
    } else {
        "400 Bad Request"
    };
    let body = serde_json::to_string(&response)?;
    let raw_response = format!(
        "HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{body}",
        body.len()
    );
    stream.write_all(raw_response.as_bytes())?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use rynat_core::LinkKind;

    #[test]
    fn context_http_request_round_trips() {
        let request = ContextRequest::copy_link_with_kind(
            r"\\nas.local\Media\demo.mp4",
            Some(LinkKind::File),
        )
        .unwrap();

        let raw = build_context_http_request(&request).unwrap();
        let parsed = parse_context_http_request(&raw).unwrap();

        assert_eq!(parsed, request);
    }

    #[test]
    fn context_http_response_round_trips() {
        let response = ContextResponse::copied("http://127.0.0.1:19527/s/demo");
        let body = serde_json::to_string(&response).unwrap();
        let raw = format!(
            "HTTP/1.1 200 OK\r\nContent-Length: {}\r\n\r\n{body}",
            body.len()
        );

        assert_eq!(parse_context_http_response(&raw).unwrap(), response);
    }
}
