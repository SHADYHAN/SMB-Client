use serde::{Deserialize, Serialize};

use crate::error::{CoreError, CoreResult};
use crate::link::{LinkKind, QuickLinkTarget, normalize_remote_path};

pub const DEFAULT_TRANSFER_BUFFER_BYTES: u32 = 1024 * 1024;
pub const MAX_TRANSFER_BUFFER_BYTES: u32 = 8 * 1024 * 1024;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum TransferDirection {
    #[serde(rename = "download")]
    Download,
    #[serde(rename = "upload")]
    Upload,
    #[serde(rename = "remote_copy")]
    RemoteCopy,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct TransferPlan {
    pub direction: TransferDirection,
    pub source: TransferEndpoint,
    pub destination: TransferEndpoint,
    pub buffer_bytes: u32,
    pub requires_streaming: bool,
    pub allow_ui_memory_copy: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub enum TransferEndpoint {
    #[serde(rename = "remote")]
    Remote(QuickLinkTarget),
    #[serde(rename = "local_file")]
    LocalFile { path: String },
}

impl TransferPlan {
    pub fn download(remote: QuickLinkTarget, local_path: impl Into<String>) -> CoreResult<Self> {
        remote.validate()?;
        require_file_target(&remote)?;
        Ok(Self::new(
            TransferDirection::Download,
            TransferEndpoint::Remote(remote),
            TransferEndpoint::LocalFile {
                path: local_path.into(),
            },
        ))
    }

    pub fn upload(
        local_path: impl Into<String>,
        server_host: impl Into<String>,
        share: impl Into<String>,
        remote_path: impl AsRef<str>,
    ) -> CoreResult<Self> {
        let remote = QuickLinkTarget::new(
            server_host,
            share,
            normalize_remote_path(remote_path.as_ref()),
            None,
            LinkKind::File,
        );
        remote.validate()?;
        Ok(Self::new(
            TransferDirection::Upload,
            TransferEndpoint::LocalFile {
                path: local_path.into(),
            },
            TransferEndpoint::Remote(remote),
        ))
    }

    pub fn remote_copy(source: QuickLinkTarget, destination: QuickLinkTarget) -> CoreResult<Self> {
        source.validate()?;
        destination.validate()?;
        require_file_target(&source)?;
        require_file_target(&destination)?;
        Ok(Self::new(
            TransferDirection::RemoteCopy,
            TransferEndpoint::Remote(source),
            TransferEndpoint::Remote(destination),
        ))
    }

    fn new(
        direction: TransferDirection,
        source: TransferEndpoint,
        destination: TransferEndpoint,
    ) -> Self {
        Self {
            direction,
            source,
            destination,
            buffer_bytes: DEFAULT_TRANSFER_BUFFER_BYTES,
            requires_streaming: true,
            allow_ui_memory_copy: false,
        }
    }

    pub fn with_buffer_bytes(mut self, buffer_bytes: u32) -> Self {
        self.buffer_bytes = buffer_bytes.clamp(1, MAX_TRANSFER_BUFFER_BYTES);
        self
    }
}

fn require_file_target(target: &QuickLinkTarget) -> CoreResult<()> {
    if target.kind == LinkKind::Directory {
        return Err(CoreError::InvalidLink(
            "transfer target must be a file, not a directory".to_string(),
        ));
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::link::{LinkKind, QuickLinkTarget};

    #[test]
    fn download_plan_requires_streaming_and_disallows_ui_memory_copy() {
        let remote = QuickLinkTarget::new("nas", "Media", "/Movies/a.mp4", None, LinkKind::File);
        let plan = TransferPlan::download(remote, "/tmp/a.mp4").unwrap();

        assert_eq!(plan.direction, TransferDirection::Download);
        assert!(plan.requires_streaming);
        assert!(!plan.allow_ui_memory_copy);
        assert_eq!(plan.buffer_bytes, DEFAULT_TRANSFER_BUFFER_BYTES);
    }

    #[test]
    fn upload_plan_normalizes_remote_path() {
        let plan =
            TransferPlan::upload("/Users/a/report.pdf", "nas", "Docs", "Reports\\report.pdf")
                .unwrap();

        match plan.destination {
            TransferEndpoint::Remote(target) => {
                assert_eq!(target.path, "/Reports/report.pdf");
                assert_eq!(target.kind, LinkKind::File);
            }
            _ => panic!("expected remote destination"),
        }
    }

    #[test]
    fn transfer_rejects_directory_copy_as_file_transfer() {
        let source = QuickLinkTarget::new("nas", "Media", "/Movies", None, LinkKind::Directory);
        let destination =
            QuickLinkTarget::new("nas", "Media", "/Backup/Movies", None, LinkKind::Directory);

        let error = TransferPlan::remote_copy(source, destination).unwrap_err();

        assert!(error.to_string().contains("must be a file"));
    }

    #[test]
    fn transfer_buffer_is_capped() {
        let remote = QuickLinkTarget::new("nas", "Media", "/Movies/a.mp4", None, LinkKind::File);
        let plan = TransferPlan::download(remote, "/tmp/a.mp4")
            .unwrap()
            .with_buffer_bytes(u32::MAX);

        assert_eq!(plan.buffer_bytes, MAX_TRANSFER_BUFFER_BYTES);
    }
}
