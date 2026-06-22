use thiserror::Error;

pub type CoreResult<T> = Result<T, CoreError>;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SmbErrorCode {
    Auth,
    Cancelled,
    NotFound,
    Permission,
    Reconnectable,
    Smb,
}

impl SmbErrorCode {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::Auth => "auth",
            Self::Cancelled => "cancelled",
            Self::NotFound => "not_found",
            Self::Permission => "permission",
            Self::Reconnectable => "reconnectable",
            Self::Smb => "smb",
        }
    }
}

#[derive(Debug, Error)]
pub enum CoreError {
    #[error("invalid link: {0}")]
    InvalidLink(String),

    #[error("missing link field: {0}")]
    MissingField(&'static str),

    #[error("storage error: {0}")]
    Storage(String),

    #[error("smb error: {message}")]
    Smb { message: String, code: SmbErrorCode },

    #[error("credential error: {0}")]
    Crypto(String),

    #[error("url error: {0}")]
    Url(#[from] url::ParseError),

    #[error("json error: {0}")]
    Json(#[from] serde_json::Error),
}

impl From<rusqlite::Error> for CoreError {
    fn from(value: rusqlite::Error) -> Self {
        Self::Storage(value.to_string())
    }
}

impl CoreError {
    pub fn smb(message: impl Into<String>) -> Self {
        Self::smb_with_code(message, SmbErrorCode::Smb)
    }

    pub fn smb_with_code(message: impl Into<String>, code: SmbErrorCode) -> Self {
        Self::Smb {
            message: message.into(),
            code,
        }
    }
}

impl From<String> for CoreError {
    fn from(value: String) -> Self {
        CoreError::smb(value)
    }
}
