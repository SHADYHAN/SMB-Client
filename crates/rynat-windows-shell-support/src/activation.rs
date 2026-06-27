use rynat_core::{QuickLinkTarget, parse_quick_link};
use thiserror::Error;

use crate::ExplorerTarget;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct LinkActivationTarget {
    pub target: QuickLinkTarget,
    pub explorer: ExplorerTarget,
}

#[derive(Debug, Clone, PartialEq, Eq, Error)]
pub enum ActivationRequestError {
    #[error("request line is not a GET request")]
    NotGet,
    #[error("request path is missing")]
    MissingPath,
    #[error("request path is not a RYNAT short link")]
    NotShortLink,
    #[error("invalid link: {0}")]
    InvalidLink(String),
}

pub fn deep_link_from_local_request_line(
    request_line: &str,
) -> Result<String, ActivationRequestError> {
    let parts = request_line.split_whitespace().collect::<Vec<_>>();
    if parts.first().copied() != Some("GET") {
        return Err(ActivationRequestError::NotGet);
    }

    let raw_path = parts.get(1).ok_or(ActivationRequestError::MissingPath)?;
    let path_and_query = raw_path.trim_start_matches('/');
    if path_and_query != "s" && !path_and_query.starts_with("s/") {
        return Err(ActivationRequestError::NotShortLink);
    }

    Ok(format!("rynat://{path_and_query}"))
}

pub fn explorer_target_from_link(
    raw_link: &str,
) -> Result<LinkActivationTarget, ActivationRequestError> {
    let target = parse_quick_link(raw_link)
        .map_err(|error| ActivationRequestError::InvalidLink(error.to_string()))?;
    let explorer = ExplorerTarget::from_link_target(&target);
    Ok(LinkActivationTarget { target, explorer })
}

#[cfg(test)]
mod tests {
    use super::*;
    use rynat_core::{LinkKind, QuickLinkTarget, build_http_link};

    #[test]
    fn converts_local_short_request_to_deep_link() {
        let target = QuickLinkTarget::new(
            "nas.local",
            "Media",
            "/Movies/demo.mp4",
            None,
            LinkKind::File,
        );
        let http_url = build_http_link(19527, &target).unwrap();
        let payload = http_url.rsplit('/').next().unwrap();
        let deep_link =
            deep_link_from_local_request_line(&format!("GET /s/{payload} HTTP/1.1")).unwrap();

        assert_eq!(deep_link, format!("rynat://s/{payload}"));
    }

    #[test]
    fn resolves_file_link_to_explorer_parent_and_selection() {
        let target = QuickLinkTarget::new(
            "nas.local",
            "Media",
            "/Movies/demo.mp4",
            None,
            LinkKind::File,
        );
        let http_url = build_http_link(19527, &target).unwrap();
        let resolved = explorer_target_from_link(&http_url).unwrap();

        assert_eq!(resolved.explorer.open_path, r"\\nas.local\Media\Movies");
        assert_eq!(
            resolved.explorer.selected_path,
            Some(r"\\nas.local\Media\Movies\demo.mp4".to_string())
        );
    }
}
