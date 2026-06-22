use chrono::Utc;
use serde::{Deserialize, Serialize};
use url::Url;
use uuid::Uuid;

use crate::error::{CoreError, CoreResult};

pub const DEFAULT_PROTOCOL: &str = "rynat";
pub const DEFAULT_REDIRECT_HOST: &str = "127.0.0.1";
pub const DEFAULT_REDIRECT_PORT: u16 = 19527;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum LinkKind {
    #[serde(rename = "file")]
    File,
    #[serde(rename = "dir", alias = "directory")]
    Directory,
    #[serde(rename = "unknown")]
    Unknown,
}

impl LinkKind {
    pub fn as_param(self) -> Option<&'static str> {
        match self {
            Self::File => Some("file"),
            Self::Directory => Some("dir"),
            Self::Unknown => None,
        }
    }

    pub fn from_param(value: Option<&str>) -> Self {
        match value {
            Some("file") => Self::File,
            Some("dir") | Some("directory") => Self::Directory,
            _ => Self::Unknown,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct QuickLinkTarget {
    pub server_host: String,
    pub share: String,
    pub path: String,
    pub name: Option<String>,
    pub kind: LinkKind,
}

impl QuickLinkTarget {
    pub fn new(
        server_host: impl Into<String>,
        share: impl Into<String>,
        path: impl AsRef<str>,
        name: Option<String>,
        kind: LinkKind,
    ) -> Self {
        Self {
            server_host: server_host.into(),
            share: share.into(),
            path: normalize_remote_path(path.as_ref()),
            name,
            kind,
        }
    }

    pub fn validate(&self) -> CoreResult<()> {
        if self.server_host.trim().is_empty() {
            return Err(CoreError::MissingField("server_host"));
        }
        if self.share.trim().is_empty() {
            return Err(CoreError::MissingField("share"));
        }
        if !self.path.starts_with('/') {
            return Err(CoreError::InvalidLink(
                "path must start with '/'".to_string(),
            ));
        }
        Ok(())
    }

    pub fn open_intent(&self) -> LinkOpenIntent {
        match self.kind {
            LinkKind::File => {
                let parent = parent_remote_path(&self.path);
                LinkOpenIntent {
                    server_host: self.server_host.clone(),
                    share: self.share.clone(),
                    browse_path: parent,
                    selected_path: Some(self.path.clone()),
                    preview_path: Some(self.path.clone()),
                }
            }
            LinkKind::Directory | LinkKind::Unknown => LinkOpenIntent {
                server_host: self.server_host.clone(),
                share: self.share.clone(),
                browse_path: self.path.clone(),
                selected_path: None,
                preview_path: None,
            },
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct QuickLink {
    pub id: String,
    pub target: QuickLinkTarget,
    pub http_url: String,
    pub deep_link_url: String,
    pub created_at: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub enum LinkDispatchMode {
    WebRedirectOnly,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct LinkEndpoint {
    pub base_url: String,
    pub dispatch_mode: LinkDispatchMode,
}

impl LinkEndpoint {
    pub fn local_helper() -> Self {
        Self {
            base_url: format!("http://{DEFAULT_REDIRECT_HOST}:{DEFAULT_REDIRECT_PORT}/s"),
            dispatch_mode: LinkDispatchMode::WebRedirectOnly,
        }
    }

    pub fn web_redirect(base_url: impl Into<String>) -> Self {
        Self {
            base_url: base_url.into(),
            dispatch_mode: LinkDispatchMode::WebRedirectOnly,
        }
    }

    pub fn validate(&self) -> CoreResult<()> {
        let url = Url::parse(&self.base_url)?;
        if url.scheme() != "http" && url.scheme() != "https" {
            return Err(CoreError::InvalidLink(
                "web redirect endpoint must use http or https".to_string(),
            ));
        }
        Ok(())
    }
}

impl QuickLink {
    pub fn create(target: QuickLinkTarget) -> CoreResult<Self> {
        Self::create_with_endpoint(target, &LinkEndpoint::local_helper())
    }

    pub fn create_with_redirect_endpoint(
        target: QuickLinkTarget,
        redirect_endpoint: &str,
    ) -> CoreResult<Self> {
        Self::create_with_endpoint(
            target,
            &LinkEndpoint {
                base_url: redirect_endpoint.to_string(),
                dispatch_mode: LinkDispatchMode::WebRedirectOnly,
            },
        )
    }

    pub fn create_with_endpoint(
        target: QuickLinkTarget,
        endpoint: &LinkEndpoint,
    ) -> CoreResult<Self> {
        target.validate()?;
        endpoint.validate()?;
        Ok(Self {
            id: Uuid::new_v4().to_string(),
            http_url: build_web_redirect_link(&endpoint.base_url, &target)?,
            deep_link_url: build_deep_link(DEFAULT_PROTOCOL, &target)?,
            target,
            created_at: Utc::now().to_rfc3339(),
        })
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct LinkOpenIntent {
    pub server_host: String,
    pub share: String,
    pub browse_path: String,
    pub selected_path: Option<String>,
    pub preview_path: Option<String>,
}

pub fn normalize_remote_path(raw: &str) -> String {
    let mut path = raw.trim().replace('\\', "/");
    if path.is_empty() {
        return "/".to_string();
    }
    if !path.starts_with('/') {
        path.insert(0, '/');
    }
    while path.contains("//") {
        path = path.replace("//", "/");
    }
    if path.len() > 1 && path.ends_with('/') {
        path.pop();
    }
    path
}

pub fn build_deep_link(protocol: &str, target: &QuickLinkTarget) -> CoreResult<String> {
    target.validate()?;
    let mut url = Url::parse(&format!("{protocol}://s"))?;
    append_target_params(&mut url, target);
    Ok(url.to_string())
}

pub fn build_http_link(port: u16, target: &QuickLinkTarget) -> CoreResult<String> {
    build_web_redirect_link(&format!("http://{DEFAULT_REDIRECT_HOST}:{port}/s"), target)
}

pub fn build_web_redirect_link(
    redirect_endpoint: &str,
    target: &QuickLinkTarget,
) -> CoreResult<String> {
    target.validate()?;
    let mut url = Url::parse(redirect_endpoint)?;
    if url.scheme() != "http" && url.scheme() != "https" {
        return Err(CoreError::InvalidLink(
            "redirect endpoint must use http or https".to_string(),
        ));
    }
    append_target_params(&mut url, target);
    Ok(url.to_string())
}

pub fn parse_quick_link(input: &str) -> CoreResult<QuickLinkTarget> {
    let url = Url::parse(input)?;
    let action = match url.scheme() {
        DEFAULT_PROTOCOL => url.host_str().unwrap_or("s").trim_matches('/'),
        "http" | "https" => url.path().trim_matches('/'),
        scheme => {
            return Err(CoreError::InvalidLink(format!(
                "unsupported scheme '{scheme}'"
            )));
        }
    };

    if action != "s" && action != "open" {
        return Err(CoreError::InvalidLink(format!(
            "unsupported action '{action}'"
        )));
    }

    let params: Vec<(String, String)> = url.query_pairs().into_owned().collect();
    let value = |short: &str, long: &str| -> Option<String> {
        params
            .iter()
            .find(|(key, _)| key == short)
            .or_else(|| params.iter().find(|(key, _)| key == long))
            .map(|(_, value)| value.clone())
    };

    let server_host = value("h", "server").ok_or(CoreError::MissingField("server_host"))?;
    let share = value("s", "share").ok_or(CoreError::MissingField("share"))?;
    let path = value("p", "path").unwrap_or_else(|| "/".to_string());
    let name = value("n", "name");
    let kind = LinkKind::from_param(value("t", "type").as_deref());

    let target = QuickLinkTarget::new(server_host, share, path, name, kind);
    target.validate()?;
    Ok(target)
}

fn append_target_params(url: &mut Url, target: &QuickLinkTarget) {
    let mut pairs = url.query_pairs_mut();
    pairs
        .append_pair("h", &target.server_host)
        .append_pair("s", &target.share)
        .append_pair("p", &target.path);
    if let Some(kind) = target.kind.as_param() {
        pairs.append_pair("t", kind);
    }
}

fn parent_remote_path(path: &str) -> String {
    let path = normalize_remote_path(path);
    if path == "/" {
        return "/".to_string();
    }
    match path.rsplit_once('/') {
        Some(("", _)) => "/".to_string(),
        Some((parent, _)) => parent.to_string(),
        None => "/".to_string(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn builds_http_link_with_encoded_params() {
        let target = QuickLinkTarget::new(
            "nas.local:445",
            "共享资料",
            "/合同/2026 Q2/报价.pdf",
            Some("报价.pdf".to_string()),
            LinkKind::File,
        );

        let url = build_http_link(19527, &target).unwrap();

        assert!(url.starts_with("http://127.0.0.1:19527/s?"));
        assert!(url.contains("h=nas.local%3A445"));
        assert!(url.contains("s=%E5%85%B1%E4%BA%AB%E8%B5%84%E6%96%99"));
        assert!(url.contains("p=%2F%E5%90%88%E5%90%8C%2F2026+Q2%2F%E6%8A%A5%E4%BB%B7.pdf"));
        assert!(url.contains("t=file"));
        assert!(!url.contains("&n="));
    }

    #[test]
    fn builds_public_https_redirect_link_for_document_apps() {
        let target = QuickLinkTarget::new(
            "nas.local",
            "Media",
            "/Movies/demo.mp4",
            Some("demo.mp4".to_string()),
            LinkKind::File,
        );

        let url = build_web_redirect_link("https://links.example.com/s", &target).unwrap();

        assert!(url.starts_with("https://links.example.com/s?"));
        assert!(url.contains("h=nas.local"));
        assert!(url.contains("s=Media"));
        assert!(url.contains("p=%2FMovies%2Fdemo.mp4"));
        assert!(url.contains("t=file"));
        assert!(!url.contains("&n="));
    }

    #[test]
    fn rejects_non_web_redirect_endpoint() {
        let target = QuickLinkTarget::new("nas", "Share", "/Docs", None, LinkKind::Directory);
        let error = build_web_redirect_link("rynat://s", &target).unwrap_err();

        assert!(error.to_string().contains("http or https"));
    }

    #[test]
    fn creates_quick_link_with_https_redirect_endpoint() {
        let target = QuickLinkTarget::new("nas", "Share", "/Docs/a.pdf", None, LinkKind::File);
        let endpoint = LinkEndpoint::web_redirect("https://links.example.com/s");
        let link = QuickLink::create_with_endpoint(target, &endpoint).unwrap();

        assert!(link.http_url.starts_with("https://links.example.com/s?"));
        assert!(link.deep_link_url.starts_with("rynat://s?"));
    }

    #[test]
    fn same_target_generates_same_share_url_for_every_user() {
        let with_name = QuickLinkTarget::new(
            "nas.local",
            "Media",
            "/Movies/demo.mp4",
            Some("用户A看到的名字.mp4".to_string()),
            LinkKind::File,
        );
        let without_name = QuickLinkTarget::new(
            "nas.local",
            "Media",
            "/Movies/demo.mp4",
            None,
            LinkKind::File,
        );
        let endpoint = LinkEndpoint::local_helper();

        let first = QuickLink::create_with_endpoint(with_name, &endpoint).unwrap();
        let second = QuickLink::create_with_endpoint(without_name, &endpoint).unwrap();

        assert_ne!(first.id, second.id);
        assert_eq!(first.http_url, second.http_url);
        assert_eq!(first.deep_link_url, second.deep_link_url);
        assert!(!first.http_url.contains(&first.id));
        assert!(!first.http_url.contains("created_at"));
        assert!(!first.http_url.contains("&n="));
    }

    #[test]
    fn parses_old_short_deep_link() {
        let target =
            parse_quick_link("rynat://s?h=192.168.102.136&s=Backoffice&p=/Contracts/2024").unwrap();

        assert_eq!(target.server_host, "192.168.102.136");
        assert_eq!(target.share, "Backoffice");
        assert_eq!(target.path, "/Contracts/2024");
        assert_eq!(target.kind, LinkKind::Unknown);
    }

    #[test]
    fn parses_legacy_open_link() {
        let target = parse_quick_link(
            "rynat://open?server=nas&share=Media&path=/Movies/demo.mp4&name=demo.mp4&type=file",
        )
        .unwrap();

        assert_eq!(target.server_host, "nas");
        assert_eq!(target.share, "Media");
        assert_eq!(target.path, "/Movies/demo.mp4");
        assert_eq!(target.name.as_deref(), Some("demo.mp4"));
        assert_eq!(target.kind, LinkKind::File);
    }

    #[test]
    fn parses_web_link_with_trailing_slash_action() {
        let target = parse_quick_link("http://127.0.0.1:19527/s/?h=nas&s=Docs&p=/Reports").unwrap();

        assert_eq!(target.server_host, "nas");
        assert_eq!(target.share, "Docs");
        assert_eq!(target.path, "/Reports");
    }

    #[test]
    fn resolves_file_link_to_parent_directory_and_preview() {
        let target = QuickLinkTarget::new("nas", "Media", "/Movies/demo.mp4", None, LinkKind::File);
        let intent = target.open_intent();

        assert_eq!(intent.browse_path, "/Movies");
        assert_eq!(intent.selected_path.as_deref(), Some("/Movies/demo.mp4"));
        assert_eq!(intent.preview_path.as_deref(), Some("/Movies/demo.mp4"));
    }

    #[test]
    fn normalizes_remote_paths() {
        assert_eq!(normalize_remote_path(""), "/");
        assert_eq!(
            normalize_remote_path("folder\\child//file.txt"),
            "/folder/child/file.txt"
        );
        assert_eq!(normalize_remote_path("/folder/"), "/folder");
    }
}
