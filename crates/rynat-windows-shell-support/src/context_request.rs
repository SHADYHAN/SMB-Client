use rynat_core::LinkKind;
use serde::{Deserialize, Serialize};
use thiserror::Error;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum ContextAction {
    CopyLink,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ContextRequest {
    pub action: ContextAction,
    pub path: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub kind: Option<LinkKind>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ContextResponse {
    pub ok: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub http_url: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub message: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Error)]
pub enum ContextRequestError {
    #[error("missing context action")]
    MissingAction,
    #[error("unsupported context action '{0}'")]
    UnsupportedAction(String),
    #[error("missing selected path")]
    MissingPath,
    #[error("missing value for '{0}'")]
    MissingOptionValue(String),
    #[error("unsupported option '{0}'")]
    UnsupportedOption(String),
    #[error("unsupported kind '{0}'")]
    UnsupportedKind(String),
}

impl ContextRequest {
    pub fn copy_link(path: impl Into<String>) -> Result<Self, ContextRequestError> {
        Self::copy_link_with_kind(path, None)
    }

    pub fn copy_link_with_kind(
        path: impl Into<String>,
        kind: Option<LinkKind>,
    ) -> Result<Self, ContextRequestError> {
        let path = path.into().trim().to_string();
        if path.is_empty() {
            return Err(ContextRequestError::MissingPath);
        }

        Ok(Self {
            action: ContextAction::CopyLink,
            path,
            kind,
        })
    }

    pub fn parse_args<I, S>(args: I) -> Result<Self, ContextRequestError>
    where
        I: IntoIterator<Item = S>,
        S: Into<String>,
    {
        let mut args = args.into_iter().map(Into::into);
        let action = args.next().ok_or(ContextRequestError::MissingAction)?;
        match action.as_str() {
            "copy-link" => {
                let path = args.next().ok_or(ContextRequestError::MissingPath)?;
                let mut kind = None;
                while let Some(option) = args.next() {
                    match option.as_str() {
                        "--kind" => {
                            let value = args.next().ok_or_else(|| {
                                ContextRequestError::MissingOptionValue(option.clone())
                            })?;
                            kind = Some(parse_kind(&value)?);
                        }
                        other => return Err(ContextRequestError::UnsupportedOption(other.into())),
                    }
                }
                Self::copy_link_with_kind(path, kind)
            }
            other => Err(ContextRequestError::UnsupportedAction(other.to_string())),
        }
    }

    pub fn to_activation_payload(&self) -> Result<String, serde_json::Error> {
        serde_json::to_string(self)
    }
}

impl ContextResponse {
    pub fn copied(http_url: impl Into<String>) -> Self {
        Self {
            ok: true,
            http_url: Some(http_url.into()),
            message: Some("分享链接已复制".to_string()),
        }
    }

    pub fn failed(message: impl Into<String>) -> Self {
        Self {
            ok: false,
            http_url: None,
            message: Some(message.into()),
        }
    }
}

fn parse_kind(value: &str) -> Result<LinkKind, ContextRequestError> {
    match value {
        "file" => Ok(LinkKind::File),
        "dir" | "directory" => Ok(LinkKind::Directory),
        "unknown" => Ok(LinkKind::Unknown),
        other => Err(ContextRequestError::UnsupportedKind(other.to_string())),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_copy_link_request() {
        let request = ContextRequest::parse_args([
            "copy-link",
            r"\\nas.local\Media\demo.mp4",
            "--kind",
            "file",
        ])
        .unwrap();

        assert_eq!(request.action, ContextAction::CopyLink);
        assert_eq!(request.path, r"\\nas.local\Media\demo.mp4");
        assert_eq!(request.kind, Some(LinkKind::File));
        assert_eq!(
            request.to_activation_payload().unwrap(),
            r#"{"action":"copy_link","path":"\\\\nas.local\\Media\\demo.mp4","kind":"file"}"#
        );
    }

    #[test]
    fn rejects_missing_path() {
        let error = ContextRequest::parse_args(["copy-link"]).unwrap_err();
        assert_eq!(error, ContextRequestError::MissingPath);
    }

    #[test]
    fn rejects_unsupported_kind() {
        let error =
            ContextRequest::parse_args(["copy-link", r"\\nas.local\Media", "--kind", "thing"])
                .unwrap_err();
        assert_eq!(
            error,
            ContextRequestError::UnsupportedKind("thing".to_string())
        );
    }
}
