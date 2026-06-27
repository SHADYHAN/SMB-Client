use rynat_core::{LinkKind, QuickLinkTarget, normalize_remote_path};

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ExplorerTarget {
    pub open_path: String,
    pub selected_path: Option<String>,
}

impl ExplorerTarget {
    pub fn from_link_target(target: &QuickLinkTarget) -> Self {
        let normalized_path = normalize_remote_path(&target.path);
        match target.kind {
            LinkKind::File => {
                let parent = parent_remote_path(&normalized_path);
                Self {
                    open_path: unc_path(&target.server_host, &target.share, &parent),
                    selected_path: Some(unc_path(
                        &target.server_host,
                        &target.share,
                        &normalized_path,
                    )),
                }
            }
            LinkKind::Directory | LinkKind::Unknown => Self {
                open_path: unc_path(&target.server_host, &target.share, &normalized_path),
                selected_path: None,
            },
        }
    }

    pub fn explorer_select_argument(&self) -> Option<String> {
        self.selected_path
            .as_ref()
            .map(|selected| format!("/select,{selected}"))
    }
}

pub fn unc_path(host: &str, share: &str, remote_path: &str) -> String {
    let normalized = normalize_remote_path(remote_path);
    let tail = normalized.trim_matches('/').replace('/', "\\");
    if tail.is_empty() {
        format!(r"\\{}\{}", host.trim(), share.trim())
    } else {
        format!(r"\\{}\{}\{}", host.trim(), share.trim(), tail)
    }
}

fn parent_remote_path(path: &str) -> String {
    let normalized = normalize_remote_path(path);
    if normalized == "/" {
        return "/".to_string();
    }

    let trimmed = normalized.trim_end_matches('/');
    match trimmed.rsplit_once('/') {
        Some(("", _)) | None => "/".to_string(),
        Some((parent, _)) => normalize_remote_path(parent),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn directory_target_opens_directory_unc_path() {
        let target = QuickLinkTarget::new(
            "192.168.102.136",
            "共享资料",
            "/项目/设计",
            None,
            LinkKind::Directory,
        );

        let explorer = ExplorerTarget::from_link_target(&target);

        assert_eq!(explorer.open_path, r"\\192.168.102.136\共享资料\项目\设计");
        assert_eq!(explorer.selected_path, None);
    }

    #[test]
    fn file_target_opens_parent_and_keeps_selected_path() {
        let target = QuickLinkTarget::new(
            "nas.local",
            "Media",
            "/Movies/demo.mp4",
            None,
            LinkKind::File,
        );

        let explorer = ExplorerTarget::from_link_target(&target);

        assert_eq!(explorer.open_path, r"\\nas.local\Media\Movies");
        assert_eq!(
            explorer.selected_path,
            Some(r"\\nas.local\Media\Movies\demo.mp4".to_string())
        );
        assert_eq!(
            explorer.explorer_select_argument(),
            Some(r"/select,\\nas.local\Media\Movies\demo.mp4".to_string())
        );
    }
}
