use rynat_core::{LinkKind, QuickLinkTarget, normalize_remote_path};
use thiserror::Error;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UncPath {
    pub host: String,
    pub share: String,
    pub remote_path: String,
    pub kind: LinkKind,
}

#[derive(Debug, Clone, PartialEq, Eq, Error)]
pub enum UncPathError {
    #[error("path is not a UNC path")]
    NotUnc,
    #[error("UNC path is missing host")]
    MissingHost,
    #[error("UNC path is missing share")]
    MissingShare,
}

impl UncPath {
    pub fn parse(raw: &str, kind: LinkKind) -> Result<Self, UncPathError> {
        let normalized = raw.trim().replace('/', "\\");
        if !normalized.starts_with("\\\\") {
            return Err(UncPathError::NotUnc);
        }

        let rest = normalized.trim_start_matches('\\');
        let mut parts = rest.split('\\').filter(|part| !part.is_empty());
        let host = parts.next().ok_or(UncPathError::MissingHost)?.trim();
        if host.is_empty() {
            return Err(UncPathError::MissingHost);
        }

        let share = parts.next().ok_or(UncPathError::MissingShare)?.trim();
        if share.is_empty() {
            return Err(UncPathError::MissingShare);
        }

        let tail = parts.collect::<Vec<_>>().join("/");
        let remote_path = normalize_remote_path(&tail);

        Ok(Self {
            host: host.trim_end_matches('.').to_ascii_lowercase(),
            share: share.to_string(),
            remote_path,
            kind,
        })
    }

    pub fn to_link_target(&self) -> QuickLinkTarget {
        QuickLinkTarget::new(
            self.host.clone(),
            self.share.clone(),
            self.remote_path.clone(),
            self.name(),
            self.kind,
        )
    }

    pub fn name(&self) -> Option<String> {
        if self.remote_path == "/" {
            return Some(self.share.clone());
        }

        self.remote_path
            .trim_matches('/')
            .rsplit('/')
            .find(|part| !part.is_empty())
            .map(ToOwned::to_owned)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_file_unc_path_to_link_target() {
        let parsed = UncPath::parse(r"\\NAS.local\Media\Movies\demo.mp4", LinkKind::File).unwrap();

        assert_eq!(parsed.host, "nas.local");
        assert_eq!(parsed.share, "Media");
        assert_eq!(parsed.remote_path, "/Movies/demo.mp4");
        assert_eq!(parsed.name(), Some("demo.mp4".to_string()));

        let target = parsed.to_link_target();
        assert_eq!(target.server_host, "nas.local");
        assert_eq!(target.share, "Media");
        assert_eq!(target.path, "/Movies/demo.mp4");
        assert_eq!(target.kind, LinkKind::File);
    }

    #[test]
    fn parses_share_root_as_directory() {
        let parsed = UncPath::parse(r"\\192.168.102.136\共享资料", LinkKind::Directory).unwrap();

        assert_eq!(parsed.host, "192.168.102.136");
        assert_eq!(parsed.share, "共享资料");
        assert_eq!(parsed.remote_path, "/");
        assert_eq!(parsed.name(), Some("共享资料".to_string()));
    }

    #[test]
    fn rejects_non_unc_paths() {
        let error = UncPath::parse(r"C:\Users\demo.txt", LinkKind::File).unwrap_err();
        assert_eq!(error, UncPathError::NotUnc);
    }
}
