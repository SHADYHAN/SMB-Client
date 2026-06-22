use chrono::Utc;
use serde::{Deserialize, Serialize};
use url::Url;
use uuid::Uuid;

use crate::error::{CoreError, CoreResult};
use crate::link::QuickLinkTarget;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum AuthMode {
    CurrentUser,
    UsernamePassword,
    Guest,
}

impl AuthMode {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::CurrentUser => "current_user",
            Self::UsernamePassword => "username_password",
            Self::Guest => "guest",
        }
    }

    pub fn from_storage_value(value: &str) -> Self {
        match value {
            "current_user" => Self::CurrentUser,
            "guest" => Self::Guest,
            _ => Self::UsernamePassword,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Default)]
#[serde(rename_all = "snake_case")]
pub enum SmbDialectPreference {
    Smb3Only,
    #[default]
    Smb3Preferred,
    Auto,
}

impl SmbDialectPreference {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::Smb3Only => "smb3_only",
            Self::Smb3Preferred => "smb3_preferred",
            Self::Auto => "auto",
        }
    }

    pub fn from_storage_value(value: &str) -> Self {
        match value {
            "smb3_only" => Self::Smb3Only,
            "auto" => Self::Auto,
            _ => Self::Smb3Preferred,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ServerEndpointKey {
    pub host: String,
    pub port: Option<u16>,
}

impl ServerEndpointKey {
    pub fn parse(raw: &str) -> CoreResult<Self> {
        parse_server_endpoint(raw)
    }

    pub fn as_link_host(&self) -> String {
        match self.port {
            Some(port) => format!("{}:{port}", self.host),
            None => self.host.clone(),
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ServerProfile {
    pub id: String,
    pub display_name: String,
    pub endpoint: ServerEndpointKey,
    pub username: Option<String>,
    pub auth_mode: AuthMode,
    pub dialect_preference: SmbDialectPreference,
    pub created_at: String,
    pub updated_at: String,
}

impl ServerProfile {
    pub fn new(
        display_name: impl Into<String>,
        host: impl AsRef<str>,
        username: Option<String>,
        auth_mode: AuthMode,
    ) -> CoreResult<Self> {
        let endpoint = ServerEndpointKey::parse(host.as_ref())?;
        let now = Utc::now().to_rfc3339();
        let display_name = normalize_display_name(display_name.into(), &endpoint);

        Ok(Self {
            id: Uuid::new_v4().to_string(),
            display_name,
            endpoint,
            username: username.and_then(|value| {
                let trimmed = value.trim().to_string();
                (!trimmed.is_empty()).then_some(trimmed)
            }),
            auth_mode,
            dialect_preference: SmbDialectPreference::default(),
            created_at: now.clone(),
            updated_at: now,
        })
    }

    pub fn link_host(&self) -> String {
        self.endpoint.as_link_host()
    }

    pub fn update(
        &mut self,
        display_name: impl Into<String>,
        host: impl AsRef<str>,
        username: Option<String>,
        auth_mode: AuthMode,
        dialect_preference: SmbDialectPreference,
    ) -> CoreResult<()> {
        let endpoint = ServerEndpointKey::parse(host.as_ref())?;
        self.display_name = normalize_display_name(display_name.into(), &endpoint);
        self.endpoint = endpoint;
        self.username = username.and_then(|value| {
            let trimmed = value.trim().to_string();
            (!trimmed.is_empty()).then_some(trimmed)
        });
        self.auth_mode = auth_mode;
        self.dialect_preference = dialect_preference;
        self.updated_at = Utc::now().to_rfc3339();
        Ok(())
    }

    pub fn matches_target(&self, target: &QuickLinkTarget) -> bool {
        ServerEndpointKey::parse(&target.server_host)
            .map(|endpoint| endpoint == self.endpoint)
            .unwrap_or(false)
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ServerCredential {
    pub server_profile_id: String,
    pub username: String,
    pub password: String,
    pub remember_password: bool,
    pub auto_login: bool,
    pub updated_at: String,
}

impl ServerCredential {
    pub fn new(
        server_profile_id: impl Into<String>,
        username: impl Into<String>,
        password: impl Into<String>,
        remember_password: bool,
        auto_login: bool,
    ) -> CoreResult<Self> {
        let server_profile_id = server_profile_id.into().trim().to_string();
        let username = username.into().trim().to_string();
        let password = password.into();
        if server_profile_id.is_empty() {
            return Err(CoreError::MissingField("server_profile_id"));
        }
        if username.is_empty() {
            return Err(CoreError::MissingField("username"));
        }
        if password.is_empty() {
            return Err(CoreError::MissingField("password"));
        }
        Ok(Self {
            server_profile_id,
            username,
            password,
            remember_password,
            auto_login,
            updated_at: Utc::now().to_rfc3339(),
        })
    }
}

pub fn parse_server_endpoint(raw: &str) -> CoreResult<ServerEndpointKey> {
    let trimmed = raw.trim();
    if trimmed.is_empty() {
        return Err(CoreError::MissingField("server_host"));
    }

    let candidate = if trimmed.contains("://") {
        trimmed.to_string()
    } else {
        format!("smb://{trimmed}")
    };
    let url = Url::parse(&candidate)?;
    let host = url
        .host_str()
        .ok_or(CoreError::MissingField("server_host"))?
        .trim()
        .trim_end_matches('.')
        .to_ascii_lowercase();

    if host.is_empty() {
        return Err(CoreError::MissingField("server_host"));
    }

    Ok(ServerEndpointKey {
        host,
        port: url.port(),
    })
}

fn normalize_display_name(display_name: String, endpoint: &ServerEndpointKey) -> String {
    let trimmed = display_name.trim();
    if trimmed.is_empty() {
        endpoint.as_link_host()
    } else {
        trimmed.to_string()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::link::{LinkKind, QuickLinkTarget};

    #[test]
    fn normalizes_server_endpoint_for_matching() {
        assert_eq!(
            parse_server_endpoint("smb://NAS.local:445/Media").unwrap(),
            ServerEndpointKey {
                host: "nas.local".to_string(),
                port: Some(445)
            }
        );
        assert_eq!(
            parse_server_endpoint("NAS.local.").unwrap(),
            ServerEndpointKey {
                host: "nas.local".to_string(),
                port: None
            }
        );
    }

    #[test]
    fn server_profile_matches_link_target_without_user_identity() {
        let profile = ServerProfile::new(
            "设计部 NAS",
            "smb://nas.local:445",
            Some("alice".to_string()),
            AuthMode::UsernamePassword,
        )
        .unwrap();
        let target = QuickLinkTarget::new(
            "NAS.local:445",
            "Media",
            "/Movies/a.mp4",
            None,
            LinkKind::File,
        );

        assert!(profile.matches_target(&target));
        assert_eq!(profile.link_host(), "nas.local:445");
    }

    #[test]
    fn updates_profile_without_replacing_identity() {
        let mut profile =
            ServerProfile::new("NAS", "nas.local", None, AuthMode::UsernamePassword).unwrap();
        let original_id = profile.id.clone();
        let created_at = profile.created_at.clone();

        profile
            .update(
                "共享网盘",
                "smb://192.168.102.136:445",
                Some("alice".to_string()),
                AuthMode::UsernamePassword,
                SmbDialectPreference::Smb3Preferred,
            )
            .unwrap();

        assert_eq!(profile.id, original_id);
        assert_eq!(profile.created_at, created_at);
        assert_eq!(profile.display_name, "共享网盘");
        assert_eq!(profile.link_host(), "192.168.102.136:445");
        assert_eq!(profile.username.as_deref(), Some("alice"));
    }
}
